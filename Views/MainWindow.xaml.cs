using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using CortexQR.Services;
using CortexQR.Helpers;

namespace CortexQR.Views
{
    public partial class MainWindow : Window
    {
        private Bitmap? _currentQrBitmap;
        private readonly string _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
        private readonly QrGenerationService _qrService;
        private bool _isResetting;

        public MainWindow()
        {
            InitializeComponent();
            _qrService = new QrGenerationService();
            LoadConfig();

            if (!string.IsNullOrWhiteSpace(DataTextBox.Text))
                GenerateQrCode();
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

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            _isResetting = true;
            try
            {
                DataTextBox.Clear();
                LogoPathTextBox.Clear();
                SaveConfig(string.Empty);

                LogoSizeSlider.Value = 15;
                LogoBackgroundCheckBox.IsChecked = false;

                FgColorRow.SelectedColor = System.Windows.Media.Colors.Black;
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

            string data = DataTextBox.Text;

            // Show/hide the empty-state hint
            EmptyHint.Visibility = string.IsNullOrWhiteSpace(data)
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(data))
            {
                QrCodeImage.Source = null;
                SaveButton.IsEnabled = false;
                DisposeBitmap();
                return;
            }

            try
            {
                // Read colors from the three ColorRow controls
                System.Windows.Media.Color fgColor     = FgColorRow.SelectedColor;
                System.Windows.Media.Color bgColor     = BgColorRow.SelectedColor;
                System.Windows.Media.Color finderColor = FinderColorRow.SelectedColor;
                System.Windows.Media.Color innerEyeColor = InnerEyeColorRow.SelectedColor;

                string shape          = (ShapeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Squares";
                string eyeShape       = (EyeShapeComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Square";
                int    logoSizePercent = (int)LogoSizeSlider.Value;
                bool   addLogoBg      = LogoBackgroundCheckBox.IsChecked == true;
                string logoPath       = LogoPathTextBox.Text;

                DisposeBitmap();

                _currentQrBitmap = _qrService.GenerateCustomQrCode(
                    data, fgColor, bgColor, finderColor, innerEyeColor, shape, eyeShape, logoPath, logoSizePercent, addLogoBg);

                if (_currentQrBitmap != null)
                {
                    QrCodeImage.Source = ImageHelper.ConvertBitmapToBitmapSource(_currentQrBitmap);
                    SaveButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating QR Code: {ex.Message}");
            }
        }

        // ── Save ──────────────────────────────────────────────────────────────

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

        private void DisposeBitmap()
        {
            if (_currentQrBitmap == null) return;
            _currentQrBitmap.Dispose();
            _currentQrBitmap = null;
        }
    }
}
