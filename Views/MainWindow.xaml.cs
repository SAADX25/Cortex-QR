using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using CortexQR.Models;
using CortexQR.Services;
using CortexQR.Helpers;
using CortexQR.ViewModels;
using QRCoder;

namespace CortexQR.Views
{
    public partial class MainWindow : Window
    {
        private Bitmap? _currentQrBitmap;
        private readonly string _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
        private readonly QrGenerationService _qrService;
        private readonly PresetStorage _presetStorage;
        private readonly ObservableCollection<PresetInfo> _presetItems = new();
        private bool _isResetting;
        private readonly BatchProcessingViewModel _batchViewModel;

        public MainWindow()
        {
            InitializeComponent();
            _qrService = new QrGenerationService();
            _presetStorage = new PresetStorage();
            var fileDialogs = new FileDialogService();
            var messageDialogs = new MessageDialogService();
            _batchViewModel = new BatchProcessingViewModel(_qrService, fileDialogs, messageDialogs, CaptureBatchRenderSettings);
            PresetComboBox.ItemsSource = _presetItems;
            LoadConfig();
            RefreshPresetList(selectNewest: false);

            BatchPanel.DataContext = _batchViewModel;

            if (!string.IsNullOrWhiteSpace(BuildPayload()))
                GenerateQrCode();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            DisposeBitmap();
        }

        // ── Config Persistence ────────────────────────────────────────────────

        private void LoadConfig()
        {
            if (!File.Exists(_configFilePath)) return;
            try
            {
                string savedPath = File.ReadAllText(_configFilePath);
                if (File.Exists(savedPath))
                    LogoPathTextBox.Text = savedPath;
            }
            catch { /* Ignore read errors */ }
        }

        private void SaveConfig(string path)
        {
            try { File.WriteAllText(_configFilePath, path); }
            catch { /* Ignore write errors */ }
        }

        // ── UI Event Handlers ─────────────────────────────────────────────────

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Logo Image",
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp|All Files|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                LogoPathTextBox.Text = dlg.FileName;
                SaveConfig(dlg.FileName);
            }
        }

        // Handles TextBox / Slider / ComboBox / CheckBox changes
        private void SettingChanged(object sender, RoutedEventArgs e)
        {
            if (_isResetting) return;
            GenerateQrCode();
        }

        // Handles ColorRow.ColorChanged (routed through EventArgs, not RoutedEventArgs)
        private void ColorRow_ColorChanged(object? sender, EventArgs e)
        {
            if (_isResetting) return;
            GenerateQrCode();
        }

        private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        // ── Sidebar Navigation ────────────────────────────────────────────

        private void NavButton_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            if (sender is System.Windows.Controls.RadioButton rb)
                SidebarNavigate(rb.Tag?.ToString() ?? "Generator");
        }

        private void SidebarNavigate(string section)
        {
            GeneratorPanel.Visibility = section == "Generator" ? Visibility.Visible : Visibility.Collapsed;
            BatchPanel.Visibility     = section == "Batch"     ? Visibility.Visible : Visibility.Collapsed;
            PresetsPanel.Visibility   = section == "Presets"   ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _isResetting = true;
            try
            {
                DataTextBox.Clear();
                WifiSsidTextBox.Clear();
                WifiPasswordBox.Clear();
                WifiAuthComboBox.SelectedIndex = 1;
                WifiHiddenCheckBox.IsChecked = false;
                VCardFullNameTextBox.Clear();
                VCardPhoneTextBox.Clear();
                VCardEmailTextBox.Clear();
                VCardCompanyTextBox.Clear();
                PayloadTabControl.SelectedIndex = 0;
                LogoPathTextBox.Clear();
                SaveConfig(string.Empty);

                LogoSizeSlider.Value = 15;
                LogoBackgroundCheckBox.IsChecked = false;

                FgColorRow.SelectedColor = System.Windows.Media.Colors.Black;
                FgColor2Row.SelectedColor = System.Windows.Media.Color.FromRgb(63, 81, 181);
                UseGradientCheckBox.IsChecked = false;
                BgColorRow.SelectedColor = System.Windows.Media.Colors.White;
                FinderColorRow.SelectedColor = System.Windows.Media.Colors.Black;
                InnerEyeColorRow.SelectedColor = System.Windows.Media.Colors.Black;

                ShapeComboBox.SelectedIndex = 0;
                EyeShapeComboBox.SelectedIndex = 0;
            }
            finally
            {
                _isResetting = false;
            }

            GenerateQrCode();
        }

        // ── QR Generation ─────────────────────────────────────────────────────

        private void GenerateQrCode()
        {
            if (!IsLoaded) return;

            string data = BuildPayload();

            // Show/hide the empty-state hint
            EmptyHint.Visibility = string.IsNullOrWhiteSpace(data)
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(data))
            {
                QrCodeImage.Source = null;
                SaveButton.IsEnabled = false;
                ExportSvgButton.IsEnabled = false;
                DisposeBitmap();
                return;
            }

            try
            {
                // Read colors from the three ColorRow controls
                System.Windows.Media.Color fgColor     = FgColorRow.SelectedColor;
                System.Windows.Media.Color fg2Color    = FgColor2Row.SelectedColor;
                System.Windows.Media.Color bgColor     = BgColorRow.SelectedColor;
                System.Windows.Media.Color finderColor = FinderColorRow.SelectedColor;
                System.Windows.Media.Color innerEyeColor = InnerEyeColorRow.SelectedColor;
                bool   useGradient    = UseGradientCheckBox.IsChecked == true;

                string shape          = (ShapeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Squares";
                string eyeShape       = (EyeShapeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Square";
                int    logoSizePercent = (int)LogoSizeSlider.Value;
                bool   addLogoBg      = LogoBackgroundCheckBox.IsChecked == true;
                string logoPath       = LogoPathTextBox.Text;

                DisposeBitmap();

                _currentQrBitmap = _qrService.GenerateCustomQrCode(
                    data, fgColor, fg2Color, useGradient, bgColor, finderColor, innerEyeColor, shape, eyeShape, logoPath, logoSizePercent, addLogoBg);

                if (_currentQrBitmap != null)
                {
                    QrCodeImage.Source = ImageHelper.ConvertBitmapToBitmapSource(_currentQrBitmap);
                    SaveButton.IsEnabled = true;
                    ExportSvgButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating QR Code: {ex.Message}");
            }
        }

        // ── Save ──────────────────────────────────────────────────────────────

        // Payload helpers

        private string BuildPayload()
        {
            return PayloadTabControl.SelectedIndex switch
            {
                1 => BuildWifiPayload(),
                2 => BuildVCardPayload(),
                _ => BuildTextOrUrlPayload()
            };
        }

        private string BuildTextOrUrlPayload()
        {
            string text = DataTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return IsLikelyUrl(text)
                ? new PayloadGenerator.Url(text).ToString()
                : text;
        }

        private string BuildWifiPayload()
        {
            string ssid = WifiSsidTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(ssid))
                return string.Empty;

            var authentication = GetWifiAuthenticationMode();
            string password = authentication == PayloadGenerator.WiFi.Authentication.nopass
                ? string.Empty
                : WifiPasswordBox.Password;

            return new PayloadGenerator.WiFi(
                ssid,
                password,
                authentication,
                WifiHiddenCheckBox.IsChecked == true,
                false).ToString();
        }

        private string BuildVCardPayload()
        {
            string fullName = VCardFullNameTextBox.Text.Trim();
            string phone = VCardPhoneTextBox.Text.Trim();
            string email = VCardEmailTextBox.Text.Trim();
            string company = VCardCompanyTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(fullName) &&
                string.IsNullOrWhiteSpace(phone) &&
                string.IsNullOrWhiteSpace(email) &&
                string.IsNullOrWhiteSpace(company))
            {
                return string.Empty;
            }

            (string firstName, string lastName) = SplitFullName(fullName);

            return new PayloadGenerator.ContactData(
                PayloadGenerator.ContactData.ContactOutputType.VCard3,
                firstName,
                lastName,
                fullName,
                phone,
                string.Empty,
                string.Empty,
                email,
                null,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                PayloadGenerator.ContactData.AddressOrder.Default,
                string.Empty,
                company,
                PayloadGenerator.ContactData.AddressType.WorkPreferred).ToString();
        }

        private PayloadGenerator.WiFi.Authentication GetWifiAuthenticationMode()
        {
            string selectedAuth = (WifiAuthComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "WPA";
            return selectedAuth switch
            {
                "WEP" => PayloadGenerator.WiFi.Authentication.WEP,
                "WPA2" => PayloadGenerator.WiFi.Authentication.WPA2,
                "No Password" => PayloadGenerator.WiFi.Authentication.nopass,
                _ => PayloadGenerator.WiFi.Authentication.WPA
            };
        }

        private static bool IsLikelyUrl(string text)
        {
            if (Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return true;
            }

            return text.Contains('.') && !text.Contains(' ') && !text.Contains('\n') && !text.Contains('\r');
        }

        private static (string FirstName, string LastName) SplitFullName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return (string.Empty, string.Empty);

            int lastSpaceIndex = fullName.LastIndexOf(' ');
            if (lastSpaceIndex <= 0 || lastSpaceIndex == fullName.Length - 1)
                return (fullName, string.Empty);

            return (
                fullName[..lastSpaceIndex].Trim(),
                fullName[(lastSpaceIndex + 1)..].Trim());
        }

        // Save

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentQrBitmap == null)
            {
                MessageBox.Show("No QR code generated yet.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Save QR Code",
                Filter = "PNG Image|*.png|JPEG Image|*.jpg",
                FileName = "QRCode"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                if (dlg.FilterIndex == 1)
                    _currentQrBitmap.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Png);
                else
                    _currentQrBitmap.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Jpeg);

                MessageBox.Show("QR Code saved successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving file: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void ExportSvgButton_Click(object sender, RoutedEventArgs e)
        {
            string data = BuildPayload();
            if (string.IsNullOrWhiteSpace(data))
            {
                MessageBox.Show("No QR code generated yet.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "Export QR Code as SVG",
                Filter = "SVG Vector|*.svg",
                FileName = "QRCode"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                System.Windows.Media.Color fgColor = FgColorRow.SelectedColor;
                System.Windows.Media.Color fg2Color = FgColor2Row.SelectedColor;
                System.Windows.Media.Color bgColor = BgColorRow.SelectedColor;
                System.Windows.Media.Color finderColor = FinderColorRow.SelectedColor;
                System.Windows.Media.Color innerEyeColor = InnerEyeColorRow.SelectedColor;

                string shape = (ShapeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Squares";
                string eyeShape = (EyeShapeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Square";
                int logoSizePercent = (int)LogoSizeSlider.Value;
                bool addLogoBg = LogoBackgroundCheckBox.IsChecked == true;
                bool useGradient = UseGradientCheckBox.IsChecked == true;
                string logoPath = LogoPathTextBox.Text;

                string svg = _qrService.GenerateCustomQrCodeSvg(
                    data,
                    fgColor,
                    fg2Color,
                    useGradient,
                    bgColor,
                    finderColor,
                    innerEyeColor,
                    shape,
                    eyeShape,
                    logoPath,
                    logoSizePercent,
                    addLogoBg);

                File.WriteAllText(dlg.FileName, svg);

                MessageBox.Show("SVG exported successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting SVG: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private BatchRenderSettings CaptureBatchRenderSettings()
        {
            return new BatchRenderSettings(
                Foreground: FgColorRow.SelectedColor,
                Foreground2: FgColor2Row.SelectedColor,
                Background: BgColorRow.SelectedColor,
                Finder: FinderColorRow.SelectedColor,
                InnerEye: InnerEyeColorRow.SelectedColor,
                UseGradient: UseGradientCheckBox.IsChecked == true,
                ModuleShape: (ShapeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Squares",
                EyeShape: (EyeShapeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Square",
                LogoPath: LogoPathTextBox.Text,
                LogoSizePercent: (int)LogoSizeSlider.Value,
                AddLogoBackground: LogoBackgroundCheckBox.IsChecked == true);
        }

        // ── Presets ─────────────────────────────────────────────────────────

        private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasSelection = PresetComboBox.SelectedItem is PresetInfo;
            PresetLoadButton.IsEnabled = hasSelection;
            if (PresetDeleteButton != null)
                PresetDeleteButton.IsEnabled = hasSelection;
        }

        private void PresetDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (PresetComboBox.SelectedItem is not PresetInfo info)
                return;

            MessageBoxResult result = MessageBox.Show(
                $"Are you sure you want to delete the preset '{info.Name}'?",
                "Delete Preset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _presetStorage.DeletePreset(info.FilePath);
                    RefreshPresetList(selectNewest: true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Unable to delete preset: {ex.Message}", "Delete Preset",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void PresetSaveButton_Click(object sender, RoutedEventArgs e)
        {
            string? defaultName = (PresetComboBox.SelectedItem as PresetInfo)?.Name;
            string? name = PromptForPresetName(defaultName);
            if (string.IsNullOrWhiteSpace(name))
                return;

            try
            {
                if (_presetStorage.PresetExists(name))
                {
                    MessageBoxResult overwrite = MessageBox.Show(
                        "A preset with this name already exists. Overwrite it?",
                        "Save Preset",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (overwrite != MessageBoxResult.Yes)
                        return;
                }

                QrStylePreset preset = BuildPresetFromUi(name);
                _presetStorage.SavePreset(preset);
                RefreshPresetList(selectName: name);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to save preset: {ex.Message}", "Save Preset",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PresetLoadButton_Click(object sender, RoutedEventArgs e)
        {
            if (PresetComboBox.SelectedItem is not PresetInfo info)
            {
                MessageBox.Show("Select a preset to load.", "Load Preset",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                QrStylePreset preset = _presetStorage.LoadPreset(info.FilePath);
                ApplyPresetToUi(preset);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to load preset: {ex.Message}", "Load Preset",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshPresetList(bool selectNewest)
        {
            string? previousName = (PresetComboBox.SelectedItem as PresetInfo)?.Name;
            _presetItems.Clear();

            foreach (PresetInfo info in _presetStorage.ListPresets())
                _presetItems.Add(info);

            if (_presetItems.Count == 0)
            {
                PresetComboBox.SelectedItem = null;
                PresetLoadButton.IsEnabled = false;
                return;
            }

            PresetInfo? target = null;
            if (!string.IsNullOrWhiteSpace(previousName))
                target = _presetItems.FirstOrDefault(item =>
                    string.Equals(item.Name, previousName, StringComparison.OrdinalIgnoreCase));

            if (target == null && selectNewest)
                target = _presetItems[0];

            PresetComboBox.SelectedItem = target;
            PresetLoadButton.IsEnabled = PresetComboBox.SelectedItem is PresetInfo;
        }

        private void RefreshPresetList(string? selectName)
        {
            _presetItems.Clear();

            foreach (PresetInfo info in _presetStorage.ListPresets())
                _presetItems.Add(info);

            PresetInfo? target = null;
            if (!string.IsNullOrWhiteSpace(selectName))
                target = _presetItems.FirstOrDefault(item =>
                    string.Equals(item.Name, selectName, StringComparison.OrdinalIgnoreCase));

            if (target == null && _presetItems.Count > 0)
                target = _presetItems[0];

            PresetComboBox.SelectedItem = target;
            PresetLoadButton.IsEnabled = PresetComboBox.SelectedItem is PresetInfo;
        }

        private QrStylePreset BuildPresetFromUi(string name)
        {
            return new QrStylePreset
            {
                Name = name,
                Foreground = ToHex(FgColorRow.SelectedColor),
                Foreground2 = ToHex(FgColor2Row.SelectedColor),
                Background = ToHex(BgColorRow.SelectedColor),
                Finder = ToHex(FinderColorRow.SelectedColor),
                InnerEye = ToHex(InnerEyeColorRow.SelectedColor),
                UseGradient = UseGradientCheckBox.IsChecked == true,
                ModuleShape = (ShapeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Squares",
                EyeShape = (EyeShapeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Square",
                LogoPath = LogoPathTextBox.Text ?? string.Empty,
                LogoSizePercent = (int)LogoSizeSlider.Value,
                AddLogoBackground = LogoBackgroundCheckBox.IsChecked == true
            };
        }

        private void ApplyPresetToUi(QrStylePreset preset)
        {
            _isResetting = true;
            try
            {
                var fallbackForeground = FgColorRow.SelectedColor;
                var fallbackForeground2 = FgColor2Row.SelectedColor;
                var fallbackBackground = BgColorRow.SelectedColor;
                var fallbackFinder = FinderColorRow.SelectedColor;
                var fallbackInnerEye = InnerEyeColorRow.SelectedColor;

                UseGradientCheckBox.IsChecked = preset.UseGradient;

                FgColorRow.SelectedColor = ParseColorOrDefault(preset.Foreground, fallbackForeground);
                FgColor2Row.SelectedColor = ParseColorOrDefault(preset.Foreground2, fallbackForeground2);
                BgColorRow.SelectedColor = ParseColorOrDefault(preset.Background, fallbackBackground);
                FinderColorRow.SelectedColor = ParseColorOrDefault(preset.Finder, fallbackFinder);
                InnerEyeColorRow.SelectedColor = ParseColorOrDefault(preset.InnerEye, fallbackInnerEye);

                SelectComboBoxItem(ShapeComboBox, preset.ModuleShape);
                SelectComboBoxItem(EyeShapeComboBox, preset.EyeShape);

                double clampedLogoSize = Math.Max(LogoSizeSlider.Minimum, Math.Min(LogoSizeSlider.Maximum, preset.LogoSizePercent));
                LogoSizeSlider.Value = clampedLogoSize;
                LogoBackgroundCheckBox.IsChecked = preset.AddLogoBackground;

                LogoPathTextBox.Text = preset.LogoPath ?? string.Empty;
                SaveConfig(LogoPathTextBox.Text);
            }
            finally
            {
                _isResetting = false;
            }

            GenerateQrCode();
        }

        private static void SelectComboBoxItem(ComboBox comboBox, string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return;

            foreach (object item in comboBox.Items)
            {
                if (item is ComboBoxItem comboItem)
                {
                    string? label = comboItem.Content?.ToString();
                    if (string.Equals(label, content, StringComparison.OrdinalIgnoreCase))
                    {
                        comboBox.SelectedItem = comboItem;
                        return;
                    }
                }
            }
        }

        private static string ToHex(System.Windows.Media.Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private static System.Windows.Media.Color ParseColorOrDefault(string? value, System.Windows.Media.Color fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            try
            {
                object? converted = System.Windows.Media.ColorConverter.ConvertFromString(value);
                if (converted is System.Windows.Media.Color color)
                    return color;
            }
            catch
            {
                return fallback;
            }

            return fallback;
        }

        private string? PromptForPresetName(string? defaultName)
        {
            var textBox = new TextBox
            {
                Text = defaultName ?? string.Empty,
                MinWidth = 240
            };

            var saveButton = new Button
            {
                Content = "Save",
                MinWidth = 80,
                IsDefault = true
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                MinWidth = 80,
                IsCancel = true,
                Margin = new Thickness(8, 0, 0, 0)
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            buttons.Children.Add(saveButton);
            buttons.Children.Add(cancelButton);

            var panel = new StackPanel
            {
                Margin = new Thickness(16)
            };
            panel.Children.Add(new TextBlock
            {
                Text = "Preset name",
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(textBox);
            panel.Children.Add(buttons);

            var dialog = new Window
            {
                Title = "Save Preset",
                Content = panel,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = this
            };

            string? result = null;
            saveButton.Click += (_, _) =>
            {
                result = textBox.Text.Trim();
                dialog.DialogResult = true;
            };

            dialog.Loaded += (_, _) =>
            {
                textBox.Focus();
                textBox.SelectAll();
            };

            bool? dialogResult = dialog.ShowDialog();
            if (dialogResult == true && !string.IsNullOrWhiteSpace(result))
                return result;

            return null;
        }

        private void DisposeBitmap()
        {
            if (_currentQrBitmap == null) return;
            _currentQrBitmap.Dispose();
            _currentQrBitmap = null;
        }
    }
}
