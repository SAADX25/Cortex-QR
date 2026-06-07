using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using CortexQR.Models;
using CortexQR.Services;
using CortexQR.Helpers;
using CortexQR.ViewModels;
using MaterialDesignThemes.Wpf;
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
            LoadBrandAssets();
            _qrService = new QrGenerationService();
            _presetStorage = new PresetStorage();
            var fileDialogs = new FileDialogService();
            var messageDialogs = new MessageDialogService();
            _batchViewModel = new BatchProcessingViewModel(_qrService, fileDialogs, messageDialogs, CaptureBatchRenderSettings);
            PresetListBox.ItemsSource = _presetItems;
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

        private void LoadBrandAssets()
        {
            try
            {
                string brandDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Brand");

                string? logoPath = FindFirstExisting(brandDirectory, "LOGO.ICO", "logo.ico");
                if (logoPath != null)
                {
                    BitmapImage logoImage = LoadBitmapImage(logoPath);
                    TitleLogoImage.Source = logoImage;
                    TitleLogoImage.Visibility = Visibility.Visible;
                    TitleLogoFallback.Visibility = Visibility.Collapsed;
                    Icon = logoImage;
                }
            }
            catch
            {
                TitleLogoImage.Visibility = Visibility.Collapsed;
                TitleLogoFallback.Visibility = Visibility.Visible;
            }
        }

        private static string? FindFirstExisting(string directory, params string[] fileNames)
        {
            foreach (string fileName in fileNames)
            {
                string path = Path.Combine(directory, fileName);
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        private static BitmapImage LoadBitmapImage(string path)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
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

        private void ClearLogoButton_Click(object sender, RoutedEventArgs e)
        {
            LogoPathTextBox.Clear();
            SaveConfig(string.Empty);
        }

        private void LogoPath_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            SettingChanged(sender, e);
            UpdateLogoDisplay();
        }

        private void UpdateLogoDisplay()
        {
            if (LogoFileNameDisplay == null || LogoFileIcon == null) return;

            string path = LogoPathTextBox.Text;
            bool hasFile = !string.IsNullOrEmpty(path) && File.Exists(path);

            LogoFileNameDisplay.Text = hasFile
                ? System.IO.Path.GetFileName(path)
                : "No file selected";

            LogoFileNameDisplay.Foreground = hasFile
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB8, 0xD8, 0xFF))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x42, 0x60));

            LogoFileIcon.Foreground = hasFile
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0A, 0x84, 0xFF))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2A, 0x40, 0x60));
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

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
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

                await ShowSuccessDialogAsync("QR code saved");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving file: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async void ExportSvgButton_Click(object sender, RoutedEventArgs e)
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

                await ShowSuccessDialogAsync("SVG exported");
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

        private void PresetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasSelection = PresetListBox.SelectedItem is PresetInfo;
            PresetLoadButton.IsEnabled = hasSelection;
        }

        private async void PresetRecordDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: PresetInfo info })
                await DeletePresetAsync(info);

            e.Handled = true;
        }

        private async Task DeletePresetAsync(PresetInfo info)
        {
            bool confirmed;
            try
            {
                confirmed = await ConfirmPresetDeleteCompactAsync(info.Name);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to show delete confirmation: {ex.Message}", "Delete Preset",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (confirmed)
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
            string? defaultName = (PresetListBox.SelectedItem as PresetInfo)?.Name;
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
            if (PresetListBox.SelectedItem is not PresetInfo info)
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
            string? previousName = (PresetListBox.SelectedItem as PresetInfo)?.Name;
            _presetItems.Clear();

            foreach (PresetInfo info in _presetStorage.ListPresets())
                _presetItems.Add(info);

            if (_presetItems.Count == 0)
            {
                PresetListBox.SelectedItem = null;
                PresetLoadButton.IsEnabled = false;
                PresetEmptyText.Visibility = Visibility.Visible;
                return;
            }

            PresetEmptyText.Visibility = Visibility.Collapsed;

            PresetInfo? target = null;
            if (!string.IsNullOrWhiteSpace(previousName))
                target = _presetItems.FirstOrDefault(item =>
                    string.Equals(item.Name, previousName, StringComparison.OrdinalIgnoreCase));

            if (target == null && selectNewest)
                target = _presetItems[0];

            PresetListBox.SelectedItem = target;
            PresetLoadButton.IsEnabled = PresetListBox.SelectedItem is PresetInfo;
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

            PresetEmptyText.Visibility = _presetItems.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            PresetListBox.SelectedItem = target;
            PresetLoadButton.IsEnabled = PresetListBox.SelectedItem is PresetInfo;
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

        private async Task ShowSuccessDialogAsync(string message)
        {
            System.Windows.Media.Color hex(string h) =>
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(h)!;

            var accentLine = new Border
            {
                Width = 4,
                CornerRadius = new CornerRadius(4),
                Background = new System.Windows.Media.SolidColorBrush(hex("#10B981")),
                Margin = new Thickness(0, 0, 12, 0),
            };

            var messageText = new TextBlock
            {
                Text = message,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(hex("#EEF4FF")),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 86,
                Height = 34,
                Style = TryFindResource("PrimaryButtonStyle") as Style,
                IsDefault = true,
                Command = DialogHost.CloseDialogCommand,
            };

            var contentGrid = new Grid();
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(accentLine, 0);
            Grid.SetColumn(messageText, 1);
            contentGrid.Children.Add(accentLine);
            contentGrid.Children.Add(messageText);

            var contentPanel = new StackPanel();
            contentPanel.Children.Add(contentGrid);
            contentPanel.Children.Add(new Border { Height = 16, Background = System.Windows.Media.Brushes.Transparent });
            contentPanel.Children.Add(okButton);

            var dialogContent = new Border
            {
                Width = 280,
                Padding = new Thickness(14),
                Background = new System.Windows.Media.SolidColorBrush(hex("#0F1520")),
                CornerRadius = new CornerRadius(10),
                BorderBrush = new System.Windows.Media.SolidColorBrush(hex("#243454")),
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = hex("#000000"),
                    BlurRadius = 22,
                    ShadowDepth = 10,
                    Opacity = 0.34,
                },
                Child = contentPanel,
            };

            okButton.HorizontalAlignment = HorizontalAlignment.Right;
            await DialogHost.Show(dialogContent, "RootDialog");
        }

        private async Task<bool> ConfirmPresetDeleteCompactAsync(string presetName)
        {
            System.Windows.Media.Color hex(string h) =>
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(h)!;

            var fileNameText = new TextBlock
            {
                Text = presetName,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(hex("#EEF4FF")),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var fileNameBox = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 9, 12, 9),
                Background = new System.Windows.Media.SolidColorBrush(hex("#0A1020")),
                BorderBrush = new System.Windows.Media.SolidColorBrush(hex("#1E3050")),
                BorderThickness = new Thickness(1),
                Child = fileNameText,
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 96,
                Height = 36,
                Style = TryFindResource("GhostButtonStyle") as Style,
                IsDefault = true,
                Command = DialogHost.CloseDialogCommand,
                CommandParameter = false,
            };

            var deleteButton = new Button
            {
                Content = "Delete",
                Width = 96,
                Height = 36,
                Style = TryFindResource("DangerButtonStyle") as Style,
                Margin = new Thickness(8, 0, 0, 0),
                Command = DialogHost.CloseDialogCommand,
                CommandParameter = true,
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0),
            };
            buttons.Children.Add(cancelButton);
            buttons.Children.Add(deleteButton);

            var contentPanel = new StackPanel();
            contentPanel.Children.Add(fileNameBox);
            contentPanel.Children.Add(buttons);

            var dialogContent = new Border
            {
                Width = 300,
                Padding = new Thickness(14),
                Background = new System.Windows.Media.SolidColorBrush(hex("#0F1520")),
                CornerRadius = new CornerRadius(10),
                BorderBrush = new System.Windows.Media.SolidColorBrush(hex("#243454")),
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = hex("#000000"),
                    BlurRadius = 22,
                    ShadowDepth = 10,
                    Opacity = 0.34,
                },
                Child = contentPanel,
            };

            object? result = await DialogHost.Show(dialogContent, "RootDialog");
            return result is bool confirmed && confirmed;
        }

        private async Task<bool> ConfirmPresetDeleteAsync(string presetName)
        {
            System.Windows.Media.Color hex(string h) =>
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(h)!;

            var accentBrush = new System.Windows.Media.LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0.5),
                EndPoint = new System.Windows.Point(1, 0.5),
            };
            accentBrush.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Colors.Transparent, 0));
            accentBrush.GradientStops.Add(new System.Windows.Media.GradientStop(hex("#F43F5E"), 0.28));
            accentBrush.GradientStops.Add(new System.Windows.Media.GradientStop(hex("#0A84FF"), 0.72));
            accentBrush.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Colors.Transparent, 1));

            var accentLine = new Border
            {
                Height = 2,
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                Background = accentBrush,
            };

            var iconBackground = new System.Windows.Media.LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 1),
            };
            iconBackground.GradientStops.Add(new System.Windows.Media.GradientStop(hex("#F43F5E"), 0));
            iconBackground.GradientStops.Add(new System.Windows.Media.GradientStop(hex("#BE185D"), 1));

            var titleIcon = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(8),
                Background = iconBackground,
                Child = new TextBlock
                {
                    Text = "!",
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            var titleText = new TextBlock
            {
                Text = "DELETE PRESET",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(hex("#7B9EC8")),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
            };

            var closeButton = new Button
            {
                Content = "X",
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                Style = TryFindResource("GhostButtonStyle") as Style,
                Foreground = new System.Windows.Media.SolidColorBrush(hex("#7B9EC8")),
                Command = DialogHost.CloseDialogCommand,
                CommandParameter = false,
            };

            var titleContent = new Grid { Margin = new Thickness(16, 12, 12, 12) };
            titleContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(titleIcon, 0);
            Grid.SetColumn(titleText, 1);
            Grid.SetColumn(closeButton, 2);
            titleContent.Children.Add(titleIcon);
            titleContent.Children.Add(titleText);
            titleContent.Children.Add(closeButton);

            var headerGrid = new Grid();
            headerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
            headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(accentLine, 0);
            Grid.SetRow(titleContent, 1);
            headerGrid.Children.Add(accentLine);
            headerGrid.Children.Add(titleContent);

            var header = new Border
            {
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                Background = new System.Windows.Media.SolidColorBrush(hex("#0A1020")),
                Child = headerGrid,
            };

            var heading = new TextBlock
            {
                Text = "Delete saved preset?",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(hex("#EEF4FF")),
                Margin = new Thickness(0, 0, 0, 8),
            };

            var bodyText = new TextBlock
            {
                Text = "This removes the styling preset from your saved list. Your current QR design will not change.",
                FontSize = 12,
                LineHeight = 18,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new System.Windows.Media.SolidColorBrush(hex("#7B9EC8")),
                Margin = new Thickness(0, 0, 0, 14),
            };

            var presetChip = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 8, 12, 8),
                Background = new System.Windows.Media.SolidColorBrush(hex("#081828")),
                BorderBrush = new System.Windows.Media.SolidColorBrush(hex("#1A2D4A")),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = presetName,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new System.Windows.Media.SolidColorBrush(hex("#B8D8FF")),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 102,
                Height = 38,
                Style = TryFindResource("GhostButtonStyle") as Style,
                IsDefault = true,
                Command = DialogHost.CloseDialogCommand,
                CommandParameter = false,
            };

            var deleteButton = new Button
            {
                Content = "Delete",
                Width = 112,
                Height = 38,
                Style = TryFindResource("DangerButtonStyle") as Style,
                Margin = new Thickness(10, 0, 0, 0),
                Command = DialogHost.CloseDialogCommand,
                CommandParameter = true,
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0),
            };
            buttons.Children.Add(cancelButton);
            buttons.Children.Add(deleteButton);

            var contentPanel = new StackPanel { Margin = new Thickness(18, 16, 18, 18) };
            contentPanel.Children.Add(heading);
            contentPanel.Children.Add(bodyText);
            contentPanel.Children.Add(presetChip);
            contentPanel.Children.Add(buttons);

            var divider = new Border
            {
                Height = 1,
                Background = new System.Windows.Media.SolidColorBrush(hex("#1A2D4A")),
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(header, 0);
            Grid.SetRow(divider, 1);
            Grid.SetRow(contentPanel, 2);
            mainGrid.Children.Add(header);
            mainGrid.Children.Add(divider);
            mainGrid.Children.Add(contentPanel);

            var dialogContent = new Border
            {
                Width = 390,
                Background = new System.Windows.Media.SolidColorBrush(hex("#0F1520")),
                CornerRadius = new CornerRadius(12),
                BorderBrush = new System.Windows.Media.SolidColorBrush(hex("#1E3050")),
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = hex("#0A84FF"),
                    BlurRadius = 36,
                    ShadowDepth = 0,
                    Opacity = 0.22,
                },
                Child = mainGrid,
            };

            object? result = await DialogHost.Show(dialogContent, "RootDialog");
            return result is bool confirmed && confirmed;
        }

        private bool ConfirmPresetDelete(string presetName)
        {
            System.Windows.Media.Color hex(string h) =>
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(h)!;

            var titleBarAccent = new System.Windows.Media.LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0.5),
                EndPoint = new System.Windows.Point(1, 0.5),
            };
            titleBarAccent.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Colors.Transparent, 0));
            titleBarAccent.GradientStops.Add(new System.Windows.Media.GradientStop(hex("#F43F5E"), 0.28));
            titleBarAccent.GradientStops.Add(new System.Windows.Media.GradientStop(hex("#0A84FF"), 0.72));
            titleBarAccent.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Colors.Transparent, 1));

            var accentLine = new Border
            {
                Height = 2,
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                Background = titleBarAccent,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = hex("#F43F5E"),
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.45,
                },
            };

            var iconBackground = new System.Windows.Media.LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 1),
            };
            iconBackground.GradientStops.Add(new System.Windows.Media.GradientStop(hex("#F43F5E"), 0));
            iconBackground.GradientStops.Add(new System.Windows.Media.GradientStop(hex("#BE185D"), 1));

            var titleIcon = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(8),
                Background = iconBackground,
                Child = new TextBlock
                {
                    Text = "!",
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            var titleText = new TextBlock
            {
                Text = "DELETE PRESET",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(hex("#7B9EC8")),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
            };

            var closeButton = new Button
            {
                Content = "X",
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                Style = TryFindResource("GhostButtonStyle") as Style,
                Foreground = new System.Windows.Media.SolidColorBrush(hex("#7B9EC8")),
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            var titleContent = new Grid { Margin = new Thickness(16, 12, 12, 12) };
            titleContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(titleIcon, 0);
            Grid.SetColumn(titleText, 1);
            Grid.SetColumn(closeButton, 2);
            titleContent.Children.Add(titleIcon);
            titleContent.Children.Add(titleText);
            titleContent.Children.Add(closeButton);

            var titleGrid = new Grid();
            titleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
            titleGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(accentLine, 0);
            Grid.SetRow(titleContent, 1);
            titleGrid.Children.Add(accentLine);
            titleGrid.Children.Add(titleContent);

            var titleBar = new Border
            {
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                Background = new System.Windows.Media.SolidColorBrush(hex("#0A1020")),
                Cursor = Cursors.SizeAll,
                Child = titleGrid,
            };

            var heading = new TextBlock
            {
                Text = "Delete saved preset?",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(hex("#EEF4FF")),
                Margin = new Thickness(0, 0, 0, 8),
            };

            var bodyText = new TextBlock
            {
                Text = "This removes the styling preset from your saved list. Your current QR design will not change.",
                FontSize = 12,
                LineHeight = 18,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new System.Windows.Media.SolidColorBrush(hex("#7B9EC8")),
                Margin = new Thickness(0, 0, 0, 14),
            };

            var presetChip = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 8, 12, 8),
                Background = new System.Windows.Media.SolidColorBrush(hex("#081828")),
                BorderBrush = new System.Windows.Media.SolidColorBrush(hex("#1A2D4A")),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = presetName,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new System.Windows.Media.SolidColorBrush(hex("#B8D8FF")),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 102,
                Height = 38,
                Style = TryFindResource("GhostButtonStyle") as Style,
                IsDefault = true,
                IsCancel = true,
            };

            var deleteButton = new Button
            {
                Content = "Delete",
                Width = 112,
                Height = 38,
                Style = TryFindResource("DangerButtonStyle") as Style,
                Margin = new Thickness(10, 0, 0, 0),
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0),
            };
            buttons.Children.Add(cancelButton);
            buttons.Children.Add(deleteButton);

            var contentPanel = new StackPanel { Margin = new Thickness(18, 16, 18, 18) };
            contentPanel.Children.Add(heading);
            contentPanel.Children.Add(bodyText);
            contentPanel.Children.Add(presetChip);
            contentPanel.Children.Add(buttons);

            var divider = new Border
            {
                Height = 1,
                Background = new System.Windows.Media.SolidColorBrush(hex("#1A2D4A")),
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(titleBar, 0);
            Grid.SetRow(divider, 1);
            Grid.SetRow(contentPanel, 2);
            mainGrid.Children.Add(titleBar);
            mainGrid.Children.Add(divider);
            mainGrid.Children.Add(contentPanel);

            var outerBorder = new Border
            {
                Width = 390,
                Background = new System.Windows.Media.SolidColorBrush(hex("#0F1520")),
                CornerRadius = new CornerRadius(12),
                BorderBrush = new System.Windows.Media.SolidColorBrush(hex("#1E3050")),
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = hex("#0A84FF"),
                    BlurRadius = 36,
                    ShadowDepth = 0,
                    Opacity = 0.22,
                },
                Child = mainGrid,
            };

            var dialog = new Window
            {
                Content = outerBorder,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Owner = this,
            };

            titleBar.MouseLeftButtonDown += (_, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                    dialog.DragMove();
            };

            bool confirmed = false;
            closeButton.Click += (_, _) => dialog.DialogResult = false;
            cancelButton.Click += (_, _) => dialog.DialogResult = false;
            deleteButton.Click += (_, _) =>
            {
                confirmed = true;
                dialog.DialogResult = true;
            };

            dialog.Loaded += (_, _) => cancelButton.Focus();

            dialog.ShowDialog();
            return confirmed;
        }

        private string? PromptForPresetName(string? defaultName)
        {
            var wm = System.Windows.Media.ColorConverter.ConvertFromString;

            System.Windows.Media.Color hex(string h) =>
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(h)!;

            // ── TextBox ──────────────────────────────────────────────────────
            var textBox = new TextBox
            {
                Text                  = defaultName ?? string.Empty,
                MinWidth              = 260,
                FontSize              = 13,
                FontFamily            = new System.Windows.Media.FontFamily("Consolas"),
                Padding               = new Thickness(10, 8, 10, 8),
                Background            = new System.Windows.Media.SolidColorBrush(hex("#0A1020")),
                Foreground            = new System.Windows.Media.SolidColorBrush(hex("#7AAAD8")),
                CaretBrush            = new System.Windows.Media.SolidColorBrush(hex("#0A84FF")),
                BorderBrush           = new System.Windows.Media.SolidColorBrush(hex("#1E3050")),
                BorderThickness       = new Thickness(1),
                SelectionBrush        = new System.Windows.Media.SolidColorBrush(hex("#0A84FF")) { Opacity = 0.4 },
            };

            // ── Save Button ──────────────────────────────────────────────────
            var saveGrad = new System.Windows.Media.LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint   = new System.Windows.Point(1, 0),
            };
            saveGrad.GradientStops.Add(new System.Windows.Media.GradientStop(hex("#0A84FF"), 0));
            saveGrad.GradientStops.Add(new System.Windows.Media.GradientStop(hex("#06B6D4"), 1));

            var saveButton = new Button
            {
                Content         = "SAVE",
                MinWidth        = 90,
                Height          = 32,
                FontWeight      = FontWeights.Bold,
                FontSize        = 11,
                Background      = saveGrad,
                Foreground      = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor          = Cursors.Hand,
                IsDefault       = true,
            };

            // ── Cancel Button ────────────────────────────────────────────────
            var cancelButton = new Button
            {
                Content         = "CANCEL",
                MinWidth        = 90,
                Height          = 32,
                FontSize        = 11,
                Background      = new System.Windows.Media.SolidColorBrush(hex("#0F1520")),
                Foreground      = new System.Windows.Media.SolidColorBrush(hex("#3A5070")),
                BorderBrush     = new System.Windows.Media.SolidColorBrush(hex("#1E3050")),
                BorderThickness = new Thickness(1),
                Cursor          = Cursors.Hand,
                Margin          = new Thickness(8, 0, 0, 0),
                IsCancel        = true,
            };

            var buttons = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin              = new Thickness(0, 14, 0, 0),
            };
            buttons.Children.Add(saveButton);
            buttons.Children.Add(cancelButton);

            // ── Label ────────────────────────────────────────────────────────
            var label = new TextBlock
            {
                Text       = "Preset name",
                FontSize   = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(hex("#5A7AA8")),
                Margin     = new Thickness(0, 0, 0, 8),
            };

            var contentPanel = new StackPanel { Margin = new Thickness(16) };
            contentPanel.Children.Add(label);
            contentPanel.Children.Add(textBox);
            contentPanel.Children.Add(buttons);

            // ── Title Bar accent line ────────────────────────────────────────
            var accentBrush = new System.Windows.Media.LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0.5),
                EndPoint   = new System.Windows.Point(1, 0.5),
            };
            accentBrush.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Colors.Transparent, 0));
            accentBrush.GradientStops.Add(new System.Windows.Media.GradientStop(hex("#0A84FF"), 0.35));
            accentBrush.GradientStops.Add(new System.Windows.Media.GradientStop(hex("#06B6D4"), 0.65));
            accentBrush.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Colors.Transparent, 1));

            var accentLine = new Border
            {
                Height        = 2,
                CornerRadius  = new CornerRadius(12, 12, 0, 0),
                Background    = accentBrush,
                Effect        = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color       = hex("#2563EB"),
                    BlurRadius  = 10,
                    ShadowDepth = 0,
                    Opacity     = 0.8,
                },
            };

            // ── Title Bar icon + text ────────────────────────────────────────
            var iconBg = new System.Windows.Media.LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint   = new System.Windows.Point(1, 1),
            };
            iconBg.GradientStops.Add(new System.Windows.Media.GradientStop(hex("#0A84FF"), 0));
            iconBg.GradientStops.Add(new System.Windows.Media.GradientStop(hex("#06B6D4"), 1));

            var iconBorder = new Border
            {
                Width        = 18,
                Height       = 18,
                CornerRadius = new CornerRadius(5),
                Background   = iconBg,
                Child        = new TextBlock
                {
                    Text                = "★",
                    FontSize            = 9,
                    Foreground          = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                },
            };

            var titleText = new TextBlock
            {
                Text              = "SAVE PRESET",
                FontSize          = 9,
                FontWeight        = FontWeights.Bold,
                Foreground        = new System.Windows.Media.SolidColorBrush(hex("#5A7AA8")),
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(8, 0, 0, 0),
            };

            var titleContent = new StackPanel
            {
                Orientation       = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Height            = 36,
                Margin            = new Thickness(14, 0, 14, 0),
            };
            titleContent.Children.Add(iconBorder);
            titleContent.Children.Add(titleText);

            var titleBarGrid = new Grid();
            titleBarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
            titleBarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(accentLine, 0);
            Grid.SetRow(titleContent, 1);
            titleBarGrid.Children.Add(accentLine);
            titleBarGrid.Children.Add(titleContent);

            var titleBar = new Border
            {
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                Background   = new System.Windows.Media.SolidColorBrush(hex("#0A1020")),
                Cursor       = Cursors.SizeAll,
                Child        = titleBarGrid,
            };

            // ── Divider ──────────────────────────────────────────────────────
            var divider = new Border
            {
                Height     = 1,
                Background = new System.Windows.Media.SolidColorBrush(hex("#1E3050")),
            };

            // ── Outer layout ─────────────────────────────────────────────────
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(titleBar,     0);
            Grid.SetRow(divider,      1);
            Grid.SetRow(contentPanel, 2);
            mainGrid.Children.Add(titleBar);
            mainGrid.Children.Add(divider);
            mainGrid.Children.Add(contentPanel);

            var outerBorder = new Border
            {
                Background      = new System.Windows.Media.SolidColorBrush(hex("#0F1520")),
                CornerRadius    = new CornerRadius(12),
                BorderBrush     = new System.Windows.Media.SolidColorBrush(hex("#1E3050")),
                BorderThickness = new Thickness(1),
                Effect          = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color       = hex("#0A84FF"),
                    BlurRadius  = 40,
                    ShadowDepth = 0,
                    Opacity     = 0.22,
                },
                Child = mainGrid,
            };

            // ── Window ───────────────────────────────────────────────────────
            var dialog = new Window
            {
                Content               = outerBorder,
                WindowStyle           = WindowStyle.None,
                AllowsTransparency    = true,
                Background            = System.Windows.Media.Brushes.Transparent,
                SizeToContent         = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode            = ResizeMode.NoResize,
                ShowInTaskbar         = false,
                Owner                 = this,
            };

            titleBar.MouseLeftButtonDown += (_, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                    dialog.DragMove();
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
