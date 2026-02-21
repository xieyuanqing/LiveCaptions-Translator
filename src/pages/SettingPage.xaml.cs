using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

using LiveCaptionsTranslator.apis;
using LiveCaptionsTranslator.models;

namespace LiveCaptionsTranslator
{
    public partial class SettingPage : Page
    {
        private static SettingWindow? SettingWindow;

        private static readonly List<KeyValuePair<AsrSourceMode, string>> AsrSourceModes =
        [
            new(AsrSourceMode.WindowsLiveCaptions, "Windows Live Captions"),
            new(AsrSourceMode.WhisperBridge, "Whisper Bridge")
        ];

        public SettingPage()
        {
            InitializeComponent();
            ApplicationThemeManager.ApplySystemTheme();
            DataContext = Translator.Setting;

            Loaded += (s, e) =>
            {
                (App.Current.MainWindow as MainWindow)?.AutoHeightAdjust(maxHeight: (int)App.Current.MainWindow.MinHeight);
                CheckForFirstUse();
                UpdateLiveCaptionsButtonState();
            };

            ASRSourceModeBox.ItemsSource = AsrSourceModes;
            ASRSourceModeBox.DisplayMemberPath = "Value";
            ASRSourceModeBox.SelectedValuePath = "Key";
            ASRSourceModeBox.SelectedValue = Translator.Setting?.ASRSourceMode;

            TranslateAPIBox.ItemsSource = Translator.Setting?.Configs.Keys;
            TranslateAPIBox.SelectedIndex = 0;

            LoadAPISetting();
            UpdateLiveCaptionsButtonState();
        }

        private void LiveCaptionsButton_click(object sender, RoutedEventArgs e)
        {
            if (!Translator.IsWindowsSourceMode || !Translator.CanControlLiveCaptionsWindow)
                return;

            bool isHidden = Translator.IsLiveCaptionsWindowHidden();
            if (isHidden)
            {
                if (Translator.TryRestoreLiveCaptionsWindow())
                    ButtonText.Text = "Hide";
            }
            else
            {
                if (Translator.TryHideLiveCaptionsWindow())
                    ButtonText.Text = "Show";
            }
        }

        private async void ASRSourceModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ASRSourceModeBox.SelectedValue is not AsrSourceMode mode)
                return;

            await Translator.SwitchCaptionSourceAsync(mode);
            UpdateLiveCaptionsButtonState();
        }

        private async void WhisperBridgeUrl_LostFocus(object sender, RoutedEventArgs e)
        {
            if (Translator.Setting?.ASRSourceMode == AsrSourceMode.WhisperBridge)
                await Translator.RestartCaptionSourceAsync();
        }

        private async void ReconnectInterval_ValueChanged(object sender, NumberBoxValueChangedEventArgs args)
        {
            if (Translator.Setting?.ASRSourceMode == AsrSourceMode.WhisperBridge)
                await Translator.RestartCaptionSourceAsync();
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
                SettingWindow.Closed += (windowSender, args) => SettingWindow = null;
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

        private void LiveCaptionsInfo_MouseEnter(object sender, MouseEventArgs e)
        {
            LiveCaptionsInfoFlyout.Show();
        }

        private void LiveCaptionsInfo_MouseLeave(object sender, MouseEventArgs e)
        {
            LiveCaptionsInfoFlyout.Hide();
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

        private void CheckForFirstUse()
        {
            if (Translator.FirstUseFlag && Translator.IsWindowsSourceMode)
                ButtonText.Text = "Hide";
        }

        private void UpdateLiveCaptionsButtonState()
        {
            if (!Translator.IsWindowsSourceMode)
            {
                ButtonText.Text = "N/A";
                return;
            }

            if (!Translator.CanControlLiveCaptionsWindow)
            {
                ButtonText.Text = "Show";
                return;
            }

            ButtonText.Text = Translator.IsLiveCaptionsWindowHidden() ? "Show" : "Hide";
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
