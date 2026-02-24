using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

using LiveCaptionsTranslator.captionSources;
using LiveCaptionsTranslator.models;

namespace LiveCaptionsTranslator
{
    public partial class SettingPage : Page
    {
        private static SettingWindow? SettingWindow;
        private string lastAppliedWhisperBridgeUrl = string.Empty;
        private bool isBridgeApplyInProgress;
        private bool pageInitialized;

        public SettingPage()
        {
            InitializeComponent();
            ApplicationThemeManager.ApplySystemTheme();
            DataContext = Translator.Setting;
            lastAppliedWhisperBridgeUrl = Translator.Setting?.WhisperBridgeUrl ?? string.Empty;

            Loaded += SettingPage_Loaded;
            Unloaded += SettingPage_Unloaded;

            TranslateAPIBox.ItemsSource = Translator.Setting?.Configs.Keys;
            TranslateAPIBox.SelectedIndex = 0;

            LoadAPISetting();
            RefreshBridgeStatus(Translator.BridgeStatus);
        }

        private void SettingPage_Loaded(object sender, RoutedEventArgs e)
        {
            (App.Current.MainWindow as MainWindow)?.AutoHeightAdjust(maxHeight: (int)App.Current.MainWindow.MinHeight);

            Translator.BridgeStatusChanged -= OnBridgeStatusChanged;
            Translator.BridgeStatusChanged += OnBridgeStatusChanged;

            RefreshBridgeStatus(Translator.BridgeStatus);
            pageInitialized = true;
        }

        private void SettingPage_Unloaded(object sender, RoutedEventArgs e)
        {
            Translator.BridgeStatusChanged -= OnBridgeStatusChanged;
        }

        private async void ApplyBridgeUrlButton_click(object sender, RoutedEventArgs e)
        {
            await ApplyBridgeUrlAsync();
        }

        private async void ReconnectBridgeButton_click(object sender, RoutedEventArgs e)
        {
            await RestartBridgeSourceAsync(showSuccessSnackbar: true);
        }

        private async void WhisperBridgeUrl_LostFocus(object sender, RoutedEventArgs e)
        {
            await ApplyBridgeUrlAsync();
        }

        private async Task ApplyBridgeUrlAsync()
        {
            if (isBridgeApplyInProgress || Translator.Setting == null)
                return;

            isBridgeApplyInProgress = true;
            SetBridgeActionEnabled(false);

            try
            {
                string previousUrl = lastAppliedWhisperBridgeUrl;
                string requestedUrl = WhisperBridgeUrlBox.Text?.Trim() ?? string.Empty;

                if (!WhisperBridgeCaptionSource.TryNormalizeBridgeUrl(
                        requestedUrl,
                        out string normalizedUrl,
                        out string validationError))
                {
                    Translator.Setting.WhisperBridgeUrl = previousUrl;
                    WhisperBridgeUrlBox.Text = previousUrl;
                    SnackbarHost.Show("Invalid bridge URL.", validationError, SnackbarType.Error, timeout: 2, closeButton: true);
                    return;
                }

                if (string.Equals(normalizedUrl, previousUrl, StringComparison.OrdinalIgnoreCase))
                {
                    Translator.Setting.WhisperBridgeUrl = normalizedUrl;
                    WhisperBridgeUrlBox.Text = normalizedUrl;
                    return;
                }

                var probe = await WhisperBridgeCaptionSource.ProbeConnectionAsync(normalizedUrl, timeoutMs: 2500);
                bool protectCurrentSession = Translator.BridgeStatus.IsConnected && !string.IsNullOrWhiteSpace(previousUrl);

                if (!probe.Success && protectCurrentSession)
                {
                    Translator.Setting.WhisperBridgeUrl = previousUrl;
                    WhisperBridgeUrlBox.Text = previousUrl;
                    SnackbarHost.Show(
                        "Bridge probe failed.",
                        $"Kept previous URL. {probe.ErrorMessage}",
                        SnackbarType.Warning,
                        timeout: 3,
                        closeButton: true);
                    return;
                }

                if (!probe.Success)
                {
                    SnackbarHost.Show(
                        "Bridge probe failed.",
                        $"Applying anyway because no active bridge session. {probe.ErrorMessage}",
                        SnackbarType.Warning,
                        timeout: 3);
                }

                Translator.Setting.WhisperBridgeUrl = normalizedUrl;
                WhisperBridgeUrlBox.Text = normalizedUrl;
                lastAppliedWhisperBridgeUrl = normalizedUrl;

                await Translator.RestartCaptionSourceAsync();
                SnackbarHost.Show("Bridge URL applied.", normalizedUrl, SnackbarType.Success, timeout: 1);
            }
            finally
            {
                SetBridgeActionEnabled(true);
                isBridgeApplyInProgress = false;
            }
        }

        private async Task RestartBridgeSourceAsync(bool showSuccessSnackbar)
        {
            if (isBridgeApplyInProgress)
                return;

            await Translator.RestartCaptionSourceAsync();
            if (showSuccessSnackbar)
            {
                SnackbarHost.Show(
                    "Bridge reconnect requested.",
                    Translator.Setting?.WhisperBridgeUrl ?? "ws://127.0.0.1:8765/captions",
                    SnackbarType.Info,
                    timeout: 1);
            }
        }

        private void SetBridgeActionEnabled(bool enabled)
        {
            ApplyBridgeUrlButton.IsEnabled = enabled;
            ReconnectBridgeButton.IsEnabled = enabled;
        }

        private void OnBridgeStatusChanged(BridgeConnectionStatus status)
        {
            Dispatcher.InvokeAsync(() => RefreshBridgeStatus(status));
        }

        private void RefreshBridgeStatus(BridgeConnectionStatus status)
        {
            string statusLabel = status.State switch
            {
                BridgeConnectionState.Connecting => "Status: Connecting",
                BridgeConnectionState.Connected => "Status: Connected",
                BridgeConnectionState.Reconnecting => $"Status: Reconnecting (attempt {Math.Max(1, status.Attempt)})",
                BridgeConnectionState.Error => "Status: Error",
                BridgeConnectionState.Stopped => "Status: Stopped",
                _ => "Status: Idle"
            };

            Brush statusBrush = status.State switch
            {
                BridgeConnectionState.Connected => Brushes.ForestGreen,
                BridgeConnectionState.Connecting => Brushes.DodgerBlue,
                BridgeConnectionState.Reconnecting => Brushes.DarkOrange,
                BridgeConnectionState.Error => Brushes.IndianRed,
                BridgeConnectionState.Stopped => Brushes.Gray,
                _ => Brushes.Gray
            };

            BridgeStatusText.Text = statusLabel;
            BridgeStatusText.Foreground = statusBrush;

            string detail = status.Message;
            if (string.IsNullOrWhiteSpace(detail))
                detail = "Bridge status unavailable.";
            if (!string.IsNullOrWhiteSpace(status.Endpoint))
                detail += $"\nEndpoint: {status.Endpoint}";

            BridgeStatusDetail.Text = detail;
        }

        private void TranslateAPIBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadAPISetting();
        }

        private void TargetLangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TargetLangBox.SelectedItem != null)
                Translator.Setting.TargetLanguage = TargetLangBox.SelectedItem.ToString();
        }

        private void TargetLangBox_LostFocus(object sender, RoutedEventArgs e)
        {
            Translator.Setting.TargetLanguage = TargetLangBox.Text;
        }

        private void APISettingButton_click(object sender, RoutedEventArgs e)
        {
            if (SettingWindow != null && SettingWindow.IsLoaded)
                SettingWindow.Activate();
            else
            {
                SettingWindow = new SettingWindow();
                SettingWindow.Closed += (sender, args) => SettingWindow = null;
                SettingWindow.Show();
            }
        }

        private void Contexts_ValueChanged(object sender, NumberBoxValueChangedEventArgs args)
        {
            if (Translator.Setting.DisplaySentences > Translator.Setting.NumContexts)
                Translator.Setting.DisplaySentences = Translator.Setting.NumContexts;
        }

        private void DisplaySentences_ValueChanged(object sender, NumberBoxValueChangedEventArgs args)
        {
            if (Translator.Setting.DisplaySentences > Translator.Setting.NumContexts)
                Translator.Setting.NumContexts = Translator.Setting.DisplaySentences;
            Translator.Caption.OnPropertyChanged("DisplayLogCards");
            Translator.Caption.OnPropertyChanged("OverlayPreviousTranslation");
        }

        private async void ReconnectInterval_ValueChanged(object sender, NumberBoxValueChangedEventArgs args)
        {
            if (!pageInitialized || isBridgeApplyInProgress)
                return;
            if (args.NewValue == args.OldValue)
                return;

            await RestartBridgeSourceAsync(showSuccessSnackbar: false);
        }

        private void BridgeInfo_MouseEnter(object sender, MouseEventArgs e)
        {
            BridgeInfoFlyout.Show();
        }

        private void BridgeInfo_MouseLeave(object sender, MouseEventArgs e)
        {
            BridgeInfoFlyout.Hide();
        }

        private void FrequencyInfo_MouseEnter(object sender, MouseEventArgs e)
        {
            FrequencyInfoFlyout.Show();
        }

        private void FrequencyInfo_MouseLeave(object sender, MouseEventArgs e)
        {
            FrequencyInfoFlyout.Hide();
        }

        private void TranslateAPIInfo_MouseEnter(object sender, MouseEventArgs e)
        {
            TranslateAPIInfoFlyout.Show();
        }

        private void TranslateAPIInfo_MouseLeave(object sender, MouseEventArgs e)
        {
            TranslateAPIInfoFlyout.Hide();
        }

        private void TargetLangInfo_MouseEnter(object sender, MouseEventArgs e)
        {
            TargetLangInfoFlyout.Show();
        }

        private void TargetLangInfo_MouseLeave(object sender, MouseEventArgs e)
        {
            TargetLangInfoFlyout.Hide();
        }

        private void CaptionLogMaxInfo_MouseEnter(object sender, MouseEventArgs e)
        {
            CaptionLogMaxInfoFlyout.Show();
        }

        private void CaptionLogMaxInfo_MouseLeave(object sender, MouseEventArgs e)
        {
            CaptionLogMaxInfoFlyout.Hide();
        }

        private void ContextAwareInfo_MouseEnter(object sender, MouseEventArgs e)
        {
            ContextAwareInfoFlyout.Show();
        }

        private void ContextAwareInfo_MouseLeave(object sender, MouseEventArgs e)
        {
            ContextAwareInfoFlyout.Hide();
        }

        public void LoadAPISetting()
        {
            var configType = Translator.Setting[Translator.Setting.ApiName].GetType();
            var languagesProp = configType.GetProperty(
                "SupportedLanguages", BindingFlags.Public | BindingFlags.Static);

            while (configType != null && languagesProp == null)
            {
                configType = configType.BaseType;
                languagesProp = configType.GetProperty(
                    "SupportedLanguages", BindingFlags.Public | BindingFlags.Static);
            }
            if (languagesProp == null)
                languagesProp = typeof(TranslateAPIConfig).GetProperty(
                    "SupportedLanguages", BindingFlags.Public | BindingFlags.Static);

            var supportedLanguages = (Dictionary<string, string>)languagesProp.GetValue(null);
            TargetLangBox.ItemsSource = supportedLanguages.Keys;

            string targetLang = Translator.Setting.TargetLanguage;
            if (!supportedLanguages.ContainsKey(targetLang))
                supportedLanguages[targetLang] = targetLang;
            TargetLangBox.SelectedItem = targetLang;
        }
    }
}
