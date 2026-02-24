using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using System.Windows.Automation;

using LiveCaptionsTranslator.apis;
using LiveCaptionsTranslator.captionSources;
using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public static class Translator
    {
        private static Caption? caption;
        private static Setting? setting;

        private static readonly ConcurrentQueue<string> pendingTextQueue = new();
        private static readonly TranslationTaskQueue translationTaskQueue = new();

        private static readonly Channel<CaptionUpdate> captionUpdateBus = Channel.CreateUnbounded<CaptionUpdate>();
        private static readonly CancellationTokenSource runtimeCts = new();
        private static readonly SemaphoreSlim sourceSwitchLock = new(1, 1);
        private static readonly object bridgeStatusLock = new();

        private static ICaptionSource? captionSource;
        private static CancellationTokenSource? sourcePumpCts;
        private static Task? sourcePumpTask;
        private static BridgeConnectionStatus bridgeStatus = new();

        private static readonly LegacyWindowsCaptionAggregator windowsAggregator = new();
        private static readonly CaptionIncrementalAggregator whisperAggregator = new();
        private static string lastQueuedWhisperText = string.Empty;

        public static Caption? Caption => caption;
        public static Setting? Setting => setting;
        public static AutomationElement? Window => (captionSource as WindowsLiveCaptionSource)?.Window;
        public static BridgeConnectionStatus BridgeStatus
        {
            get
            {
                lock (bridgeStatusLock)
                    return bridgeStatus;
            }
        }

        public static bool LogOnlyFlag { get; set; } = false;
        public static bool FirstUseFlag { get; set; } = false;

        public static event Action? TranslationLogged;
        public static event Action<BridgeConnectionStatus>? BridgeStatusChanged;

        static Translator()
        {
            if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), LiveCaptionsTranslator.models.Setting.FILENAME)))
                FirstUseFlag = true;

            caption = Caption.GetInstance();
            setting = Setting.Load();

            bridgeStatus = new BridgeConnectionStatus
            {
                State = BridgeConnectionState.Idle,
                Endpoint = setting?.WhisperBridgeUrl ?? "ws://127.0.0.1:8765/captions",
                Message = "Bridge source not started.",
                Attempt = 0,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            try
            {
                InitializeCaptionSourceAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                caption.DisplayTranslatedCaption = $"[WARNING] Caption source initialization failed: {ex.Message}";
            }
        }

        public static async Task InitializeCaptionSourceAsync()
        {
            if (Setting == null)
                return;
            await SwitchCaptionSourceInternalAsync(AsrSourceMode.WhisperBridge, forceRestart: true);
        }

        public static async Task SwitchCaptionSourceAsync(AsrSourceMode mode)
        {
            if (Setting == null)
                return;

            Setting.ASRSourceMode = AsrSourceMode.WhisperBridge;
            await SwitchCaptionSourceInternalAsync(AsrSourceMode.WhisperBridge, forceRestart: true);
        }

        public static async Task RestartCaptionSourceAsync()
        {
            if (Setting == null)
                return;
            await SwitchCaptionSourceInternalAsync(AsrSourceMode.WhisperBridge, forceRestart: true);
        }

        public static bool IsWindowsSourceMode =>
            Setting?.ASRSourceMode == AsrSourceMode.WindowsLiveCaptions;

        public static bool CanControlLiveCaptionsWindow =>
            captionSource is WindowsLiveCaptionSource windowsSource && windowsSource.Window != null;

        public static bool IsLiveCaptionsWindowHidden()
        {
            if (captionSource is not WindowsLiveCaptionSource windowsSource)
                return true;
            return windowsSource.IsWindowHidden;
        }

        public static bool TryHideLiveCaptionsWindow()
        {
            if (captionSource is not WindowsLiveCaptionSource windowsSource)
                return false;
            return windowsSource.TryHideWindow();
        }

        public static bool TryRestoreLiveCaptionsWindow()
        {
            if (captionSource is not WindowsLiveCaptionSource windowsSource)
                return false;
            return windowsSource.TryRestoreWindow();
        }

        public static async Task ShutdownAsync()
        {
            runtimeCts.Cancel();

            await sourceSwitchLock.WaitAsync();
            try
            {
                await StopCaptionSourceInternalAsync();
            }
            finally
            {
                sourceSwitchLock.Release();
            }
        }

        public static async Task SyncLoop()
        {
            var reader = captionUpdateBus.Reader;

            while (!runtimeCts.Token.IsCancellationRequested)
            {
                try
                {
                    var waitToReadTask = reader.WaitToReadAsync(runtimeCts.Token).AsTask();
                    var flushDelayTask = Task.Delay(80, runtimeCts.Token);
                    var completedTask = await Task.WhenAny(waitToReadTask, flushDelayTask);

                    if (completedTask == waitToReadTask && await waitToReadTask)
                    {
                        while (reader.TryRead(out CaptionUpdate update))
                            ProcessCaptionUpdate(update);
                    }

                    FlushWhisperByIdleTimeout();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public static async Task TranslateLoop()
        {
            while (!runtimeCts.Token.IsCancellationRequested)
            {
                if (pendingTextQueue.TryDequeue(out string? originalSnapshot) &&
                    !string.IsNullOrWhiteSpace(originalSnapshot))
                {
                    if (LogOnlyFlag)
                    {
                        bool isOverwrite = await IsOverwrite(originalSnapshot);
                        await LogOnly(originalSnapshot, isOverwrite);
                    }
                    else
                    {
                        translationTaskQueue.Enqueue(token => Task.Run(
                            () => Translate(originalSnapshot, token), token), originalSnapshot);
                    }
                }

                try
                {
                    await Task.Delay(40, runtimeCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        public static async Task DisplayLoop()
        {
            while (!runtimeCts.Token.IsCancellationRequested)
            {
                var (translatedText, isChoke) = translationTaskQueue.Output;

                if (LogOnlyFlag)
                {
                    Caption.TranslatedCaption = string.Empty;
                    Caption.DisplayTranslatedCaption = "[Paused]";
                    Caption.OverlayNoticePrefix = "[Paused]";
                    Caption.OverlayCurrentTranslation = string.Empty;
                }
                else if (!string.IsNullOrEmpty(RegexPatterns.NoticePrefix().Replace(
                             translatedText, string.Empty).Trim()) &&
                         string.CompareOrdinal(Caption.TranslatedCaption, translatedText) != 0)
                {
                    Caption.TranslatedCaption = translatedText;
                    Caption.DisplayTranslatedCaption =
                        TextUtil.ShortenDisplaySentence(Caption.TranslatedCaption, TextUtil.VERYLONG_THRESHOLD);

                    if (Caption.TranslatedCaption.Contains("[ERROR]") || Caption.TranslatedCaption.Contains("[WARNING]"))
                        Caption.OverlayCurrentTranslation = Caption.TranslatedCaption;
                    else
                    {
                        var match = RegexPatterns.NoticePrefixAndTranslation().Match(Caption.TranslatedCaption);
                        Caption.OverlayNoticePrefix = match.Groups[1].Value.Trim();
                        Caption.OverlayCurrentTranslation = match.Groups[2].Value.Trim();
                    }
                }

                if (isChoke)
                {
                    try
                    {
                        await Task.Delay(720, runtimeCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                try
                {
                    await Task.Delay(40, runtimeCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private static async Task SwitchCaptionSourceInternalAsync(AsrSourceMode mode, bool forceRestart)
        {
            mode = AsrSourceMode.WhisperBridge;

            await sourceSwitchLock.WaitAsync();
            try
            {
                bool isAlreadyRunning = captionSource != null &&
                                        ((mode == AsrSourceMode.WindowsLiveCaptions && captionSource is WindowsLiveCaptionSource) ||
                                         (mode == AsrSourceMode.WhisperBridge && captionSource is WhisperBridgeCaptionSource));
                if (isAlreadyRunning && !forceRestart)
                    return;

                await StopCaptionSourceInternalAsync();

                ICaptionSource source = mode == AsrSourceMode.WindowsLiveCaptions
                    ? new WindowsLiveCaptionSource()
                    : new WhisperBridgeCaptionSource(Setting?.WhisperBridgeUrl ?? "ws://127.0.0.1:8765/captions",
                        Setting?.ReconnectIntervalMs ?? 1500);

                if (source is WhisperBridgeCaptionSource whisperBridgeSource)
                    whisperBridgeSource.StatusChanged += OnBridgeSourceStatusChanged;

                captionSource = source;
                await source.StartAsync(runtimeCts.Token);
                StartSourcePump(source);

                ResetCaptionPipelineStates();
            }
            catch (Exception ex)
            {
                UpdateBridgeStatus(new BridgeConnectionStatus
                {
                    State = BridgeConnectionState.Error,
                    Endpoint = Setting?.WhisperBridgeUrl ?? "ws://127.0.0.1:8765/captions",
                    Message = $"Caption source switch failed: {ex.Message}",
                    Attempt = 0,
                    UpdatedAt = DateTimeOffset.UtcNow
                });

                if (caption != null)
                    caption.DisplayTranslatedCaption = $"[WARNING] Caption source switch failed: {ex.Message}";
            }
            finally
            {
                sourceSwitchLock.Release();
            }
        }

        private static async Task StopCaptionSourceInternalAsync()
        {
            if (sourcePumpCts != null)
            {
                sourcePumpCts.Cancel();
                if (sourcePumpTask != null)
                {
                    try
                    {
                        await sourcePumpTask;
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }

                sourcePumpCts.Dispose();
                sourcePumpCts = null;
                sourcePumpTask = null;
            }

            if (captionSource != null)
            {
                if (captionSource is WhisperBridgeCaptionSource whisperBridgeSource)
                    whisperBridgeSource.StatusChanged -= OnBridgeSourceStatusChanged;

                await captionSource.StopAsync(runtimeCts.Token);
                captionSource = null;
            }

            UpdateBridgeStatus(new BridgeConnectionStatus
            {
                State = BridgeConnectionState.Stopped,
                Endpoint = Setting?.WhisperBridgeUrl ?? "ws://127.0.0.1:8765/captions",
                Message = "Bridge source stopped.",
                Attempt = 0,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        private static void StartSourcePump(ICaptionSource source)
        {
            sourcePumpCts = CancellationTokenSource.CreateLinkedTokenSource(runtimeCts.Token);
            sourcePumpTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (CaptionUpdate update in source.Updates.ReadAllAsync(sourcePumpCts.Token))
                        await captionUpdateBus.Writer.WriteAsync(update, sourcePumpCts.Token);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    UpdateBridgeStatus(new BridgeConnectionStatus
                    {
                        State = BridgeConnectionState.Error,
                        Endpoint = Setting?.WhisperBridgeUrl ?? "ws://127.0.0.1:8765/captions",
                        Message = $"Caption source disconnected: {ex.Message}",
                        Attempt = 0,
                        UpdatedAt = DateTimeOffset.UtcNow
                    });

                    if (caption != null)
                        caption.DisplayTranslatedCaption = $"[WARNING] Caption source disconnected: {ex.Message}";
                }
            }, sourcePumpCts.Token);
        }

        private static void OnBridgeSourceStatusChanged(BridgeConnectionStatus status)
        {
            UpdateBridgeStatus(status);
        }

        private static void UpdateBridgeStatus(BridgeConnectionStatus status)
        {
            lock (bridgeStatusLock)
                bridgeStatus = status;

            BridgeStatusChanged?.Invoke(status);
        }

        private static void ResetCaptionPipelineStates()
        {
            windowsAggregator.Reset();
            whisperAggregator.Reset();
            lastQueuedWhisperText = string.Empty;

            while (pendingTextQueue.TryDequeue(out _))
            {
            }
        }

        private static void ProcessCaptionUpdate(CaptionUpdate update)
        {
            if (update.Source == CaptionSourceKinds.WindowsLiveCaptions)
                ProcessWindowsCaption(update.Text);
            else
                ProcessWhisperCaption(update);
        }

        private static void ProcessWindowsCaption(string fullText)
        {
            if (Setting == null || Caption == null)
                return;

            LegacyWindowsCaptionResult result = windowsAggregator.Process(fullText, new LegacyWindowsCaptionOptions
            {
                ContextCount = Caption.Contexts.Count,
                DisplaySentences = Setting.DisplaySentences,
                MaxSyncInterval = Setting.MaxSyncInterval,
                MaxIdleInterval = Setting.MaxIdleInterval
            });

            if (result.ShouldClearContexts)
                ClearContexts();

            if (!string.IsNullOrWhiteSpace(result.OverlayOriginalCaption))
                Caption.OverlayOriginalCaption = result.OverlayOriginalCaption;

            if (string.CompareOrdinal(Caption.DisplayOriginalCaption, result.DisplayOriginalCaption) != 0)
                Caption.DisplayOriginalCaption = result.DisplayOriginalCaption;

            Caption.OriginalCaption = result.CurrentOriginalCaption;

            if (!string.IsNullOrWhiteSpace(result.CommittedText))
                pendingTextQueue.Enqueue(result.CommittedText);
        }

        private static void ProcessWhisperCaption(CaptionUpdate update)
        {
            if (Setting == null || Caption == null)
                return;

            whisperAggregator.EnablePartial = Setting.EnablePartial;
            whisperAggregator.IdleFinalizeMs = Math.Clamp(Setting.MaxIdleInterval * 25, 300, 10000);

            CaptionIncrementalResult result = whisperAggregator.Process(update);
            ApplyWhisperResult(result);
        }

        private static void FlushWhisperByIdleTimeout()
        {
            if (Setting == null || Caption == null ||
                Setting.ASRSourceMode != AsrSourceMode.WhisperBridge)
            {
                return;
            }

            whisperAggregator.EnablePartial = Setting.EnablePartial;
            whisperAggregator.IdleFinalizeMs = Math.Clamp(Setting.MaxIdleInterval * 25, 300, 10000);

            CaptionIncrementalResult result = whisperAggregator.FlushIfIdle(DateTimeOffset.UtcNow);
            ApplyWhisperResult(result);
        }

        private static void ApplyWhisperResult(CaptionIncrementalResult result)
        {
            if (Setting == null || Caption == null)
                return;

            if (result.HasDisplayUpdate && !string.IsNullOrWhiteSpace(result.DisplayText))
            {
                Caption.DisplayOriginalCaption = TextUtil.ShortenDisplaySentence(
                    result.DisplayText,
                    TextUtil.VERYLONG_THRESHOLD);
                Caption.OverlayOriginalCaption = result.OverlayText;
            }

            if (!string.IsNullOrWhiteSpace(result.CommittedText))
            {
                Caption.OriginalCaption = result.CommittedText;
                if (string.CompareOrdinal(lastQueuedWhisperText, result.CommittedText) != 0)
                {
                    pendingTextQueue.Enqueue(result.CommittedText);
                    lastQueuedWhisperText = result.CommittedText;
                }
            }
            else if (Setting.EnablePartial && !string.IsNullOrWhiteSpace(result.CurrentText))
            {
                Caption.OriginalCaption = result.CurrentText;
            }
        }

        public static async Task<(string, bool)> Translate(string text, CancellationToken token = default)
        {
            string translatedText;
            bool isChoke = !string.IsNullOrEmpty(text) && Array.IndexOf(TextUtil.PUNC_EOS, text[^1]) != -1;

            try
            {
                var sw = Setting.MainWindow.LatencyShow ? Stopwatch.StartNew() : null;

                if (Setting.ContextAware && !TranslateAPI.IsLLMBased)
                {
                    translatedText = await TranslateAPI.TranslateFunction($"{Caption.AwareContextsCaption} 🔤 {text} 🔤", token);
                    translatedText = RegexPatterns.TargetSentence().Match(translatedText).Groups[1].Value;
                }
                else
                {
                    translatedText = await TranslateAPI.TranslateFunction(text, token);
                    translatedText = translatedText.Replace("🔤", "");
                }

                if (sw != null)
                {
                    sw.Stop();
                    translatedText = $"[{sw.ElapsedMilliseconds,4} ms] " + translatedText;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ($"[ERROR] Translation Failed: {ex.Message}", isChoke);
            }

            return (translatedText, isChoke);
        }

        public static async Task Log(string originalText, string translatedText,
            bool isOverwrite = false, CancellationToken token = default)
        {
            string targetLanguage, apiName;
            if (Setting != null)
            {
                targetLanguage = Setting.TargetLanguage;
                apiName = Setting.ApiName;
            }
            else
            {
                targetLanguage = "N/A";
                apiName = "N/A";
            }

            try
            {
                if (isOverwrite)
                    await SQLiteHistoryLogger.DeleteLastTranslation(token);
                await SQLiteHistoryLogger.LogTranslation(originalText, translatedText, targetLanguage, apiName);
                TranslationLogged?.Invoke();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] Logging history failed.", ex.Message, SnackbarType.Error,
                    timeout: 2, closeButton: true);
            }
        }

        public static async Task LogOnly(string originalText,
            bool isOverwrite = false, CancellationToken token = default)
        {
            try
            {
                if (isOverwrite)
                    await SQLiteHistoryLogger.DeleteLastTranslation(token);
                await SQLiteHistoryLogger.LogTranslation(originalText, "N/A", "N/A", "LogOnly");
                TranslationLogged?.Invoke();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] Logging history failed.", ex.Message, SnackbarType.Error,
                    timeout: 2, closeButton: true);
            }
        }

        public static async Task AddContexts(CancellationToken token = default)
        {
            var lastLog = await SQLiteHistoryLogger.LoadLastTranslation(token);
            if (lastLog == null)
                return;

            if (Caption?.Contexts.Count >= Caption.MAX_CONTEXTS)
                Caption.Contexts.Dequeue();
            Caption?.Contexts.Enqueue(lastLog);

            Caption?.OnPropertyChanged("DisplayLogCards");
            Caption?.OnPropertyChanged("OverlayPreviousTranslation");
        }

        public static void ClearContexts()
        {
            Caption?.Contexts.Clear();

            Caption?.OnPropertyChanged("DisplayLogCards");
            Caption?.OnPropertyChanged("OverlayPreviousTranslation");
        }

        public static async Task<bool> IsOverwrite(string originalText, CancellationToken token = default)
        {
            string lastOriginalText = await SQLiteHistoryLogger.LoadLastSourceText(token);
            if (lastOriginalText == null)
                return false;

            int minLen = Math.Min(originalText.Length, lastOriginalText.Length);
            originalText = originalText[..minLen];
            lastOriginalText = lastOriginalText[..minLen];

            double similarity = TextUtil.Similarity(originalText, lastOriginalText);
            return similarity > TextUtil.SIM_THRESHOLD;
        }
    }
}
