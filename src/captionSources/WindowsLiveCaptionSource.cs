using System.Windows;
using System.Windows.Automation;
using System.Threading.Channels;

using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator.captionSources
{
    public sealed class WindowsLiveCaptionSource : ICaptionSource
    {
        private readonly Channel<CaptionUpdate> updates = Channel.CreateUnbounded<CaptionUpdate>();
        private readonly object windowLock = new();

        private CancellationTokenSource? loopCts;
        private Task? loopTask;
        private long sequence;
        private AutomationElement? window;

        public ChannelReader<CaptionUpdate> Updates => updates.Reader;

        public AutomationElement? Window
        {
            get
            {
                lock (windowLock)
                    return window;
            }
        }

        public bool IsWindowHidden
        {
            get
            {
                var snapshot = Window;
                if (snapshot == null)
                    return true;

                try
                {
                    return snapshot.Current.BoundingRectangle == Rect.Empty;
                }
                catch
                {
                    return true;
                }
            }
        }

        public Task StartAsync(CancellationToken token = default)
        {
            if (loopTask != null && !loopTask.IsCompleted)
                return Task.CompletedTask;

            loopCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            loopTask = Task.Run(() => CaptureLoopAsync(loopCts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken token = default)
        {
            if (loopCts != null)
            {
                loopCts.Cancel();
                if (loopTask != null)
                {
                    try
                    {
                        await loopTask;
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }

                loopCts.Dispose();
                loopCts = null;
                loopTask = null;
            }

            CloseWindow();
        }

        public bool TryHideWindow()
        {
            var snapshot = Window;
            if (snapshot == null)
                return false;

            try
            {
                LiveCaptionsHandler.HideLiveCaptions(snapshot);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool TryRestoreWindow()
        {
            var snapshot = Window;
            if (snapshot == null)
                return false;

            try
            {
                LiveCaptionsHandler.RestoreLiveCaptions(snapshot);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task CaptureLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                AutomationElement? liveWindow = EnsureWindow();
                if (liveWindow == null)
                {
                    await Task.Delay(1000, token);
                    continue;
                }

                try
                {
                    _ = liveWindow.Current.Name;
                    string fullText = LiveCaptionsHandler.GetCaptions(liveWindow);
                    if (!string.IsNullOrWhiteSpace(fullText))
                    {
                        sequence++;
                        await updates.Writer.WriteAsync(new CaptionUpdate
                        {
                            Text = fullText,
                            IsFinal = false,
                            Sequence = sequence,
                            Source = CaptionSourceKinds.WindowsLiveCaptions,
                            Timestamp = DateTimeOffset.UtcNow,
                            UtteranceId = "windows-live"
                        }, token);
                    }

                    await Task.Delay(25, token);
                }
                catch (ElementNotAvailableException)
                {
                    SetWindow(null);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(200, token);
                }
            }
        }

        private AutomationElement? EnsureWindow()
        {
            var currentWindow = Window;
            if (currentWindow != null)
                return currentWindow;

            try
            {
                currentWindow = LiveCaptionsHandler.LaunchLiveCaptions();
                LiveCaptionsHandler.FixLiveCaptions(currentWindow);
                LiveCaptionsHandler.HideLiveCaptions(currentWindow);
                SetWindow(currentWindow);
                return currentWindow;
            }
            catch
            {
                return null;
            }
        }

        private void CloseWindow()
        {
            var snapshot = Window;
            if (snapshot == null)
                return;

            try
            {
                LiveCaptionsHandler.RestoreLiveCaptions(snapshot);
            }
            catch
            {
            }

            try
            {
                LiveCaptionsHandler.KillLiveCaptions(snapshot);
            }
            catch
            {
            }

            SetWindow(null);
        }

        private void SetWindow(AutomationElement? value)
        {
            lock (windowLock)
                window = value;
        }
    }
}
