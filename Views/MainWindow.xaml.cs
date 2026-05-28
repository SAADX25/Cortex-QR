using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using CortexQR.Models;
using CortexQR.Services;
using CortexQR.Helpers;
using QRCoder;
using WinForms = System.Windows.Forms;

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
        private CancellationTokenSource? _batchCts;
        private bool _isBatchRunning;

        private static readonly HashSet<string> DataHeaderNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "data",
            "payload",
            "text",
            "content",
            "url",
            "qr",
            "qrdata",
            "qrcode",
            "qrcontent",
            "message"
        };

        private static readonly HashSet<string> FileHeaderNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "filename",
            "file",
            "name",
            "title",
            "id"
        };

        private sealed record BatchItem(string Data, string FileName, int RowNumber);

        private sealed record BatchProgress(int Completed, int Total, int Errors, string? CurrentFile);

        private sealed class BatchRenderSettings
        {
            public System.Windows.Media.Color Foreground { get; init; }
            public System.Windows.Media.Color Foreground2 { get; init; }
            public System.Windows.Media.Color Background { get; init; }
            public System.Windows.Media.Color Finder { get; init; }
            public System.Windows.Media.Color InnerEye { get; init; }
            public bool UseGradient { get; init; }
            public string ModuleShape { get; init; } = "Squares";
            public string EyeShape { get; init; } = "Square";
            public int LogoSizePercent { get; init; }
            public bool AddLogoBackground { get; init; }
            public string LogoPath { get; init; } = string.Empty;
        }

        public MainWindow()
        {
            InitializeComponent();
            _qrService = new QrGenerationService();
            _presetStorage = new PresetStorage();
            PresetComboBox.ItemsSource = _presetItems;
            LoadConfig();
            RefreshPresetList(selectNewest: false);

            if (!string.IsNullOrWhiteSpace(BuildPayload()))
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

        // ── Batch Processing ────────────────────────────────────────────────

        private void BatchBrowseCsvButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select CSV File",
                Filter = "CSV Files|*.csv|All Files|*.*"
            };

            if (dlg.ShowDialog() != true) return;

            BatchCsvPathTextBox.Text = dlg.FileName;

            if (string.IsNullOrWhiteSpace(BatchOutputFolderTextBox.Text))
            {
                string? folder = Path.GetDirectoryName(dlg.FileName);
                if (!string.IsNullOrWhiteSpace(folder))
                    BatchOutputFolderTextBox.Text = folder;
            }
        }

        private void BatchBrowseOutputButton_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.FolderBrowserDialog
            {
                Description = "Select output folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                BatchOutputFolderTextBox.Text = dlg.SelectedPath;
        }

        private async void BatchStartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isBatchRunning)
                return;

            await RunBatchGenerationAsync();
        }

        private void BatchCancelButton_Click(object sender, RoutedEventArgs e)
        {
            _batchCts?.Cancel();
        }

        private async Task RunBatchGenerationAsync()
        {
            string csvPath = BatchCsvPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
            {
                MessageBox.Show("Please select a valid CSV file.", "Batch Processing",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string outputFolder = BatchOutputFolderTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                MessageBox.Show("Please select an output folder.", "Batch Processing",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Directory.CreateDirectory(outputFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to create output folder: {ex.Message}", "Batch Processing",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            bool outputSvg = string.Equals(
                (BatchOutputFormatComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString(),
                "SVG",
                StringComparison.OrdinalIgnoreCase);

            BatchRenderSettings settings = CaptureBatchRenderSettings();

            List<BatchItem> items;
            int skippedRows;
            try
            {
                items = BuildBatchItems(csvPath, out skippedRows);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"CSV parse error: {ex.Message}", "Batch Processing",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (items.Count == 0)
            {
                MessageBox.Show("No valid data rows found in the CSV.", "Batch Processing",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isBatchRunning = true;
            _batchCts = new CancellationTokenSource();
            SetBatchUiState(true);
            BatchProgressBar.Value = 0;
            BatchStatusTextBlock.Text = $"0 / {items.Count}";

            IProgress<BatchProgress> progress = new Progress<BatchProgress>(UpdateBatchProgress);
            var errors = new List<string>();
            int completed = 0;
            int errorCount = 0;
            int maxConcurrency = Math.Max(1, Math.Min(Environment.ProcessorCount, 4));
            using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var tasks = new List<Task>(items.Count);
            CancellationToken token = _batchCts.Token;

            try
            {
                foreach (BatchItem item in items)
                {
                    await semaphore.WaitAsync(token);

                    tasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            token.ThrowIfCancellationRequested();
                            GenerateAndSaveBatchItem(item, settings, outputFolder, outputSvg);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            lock (errors)
                            {
                                errors.Add($"Row {item.RowNumber}: {item.FileName} - {ex.Message}");
                            }
                            Interlocked.Increment(ref errorCount);
                        }
                        finally
                        {
                            int done = Interlocked.Increment(ref completed);
                            progress.Report(new BatchProgress(done, items.Count, errorCount, item.FileName));
                            semaphore.Release();
                        }
                    }, token));
                }

                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                if (tasks.Count > 0)
                {
                    try
                    {
                        await Task.WhenAll(tasks);
                    }
                    catch (OperationCanceledException)
                    {
                        // Ignore further cancellation during cleanup.
                    }
                }

                BatchStatusTextBlock.Text = "Batch cancelled.";
            }
            finally
            {
                _isBatchRunning = false;
                _batchCts.Dispose();
                _batchCts = null;
                SetBatchUiState(false);
            }

            if (!token.IsCancellationRequested)
            {
                if (errors.Count > 0)
                {
                    string logPath = Path.Combine(outputFolder, $"batch-errors-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                    File.WriteAllLines(logPath, errors);

                    MessageBox.Show(
                        $"Batch complete with {errors.Count} error(s). See log: {logPath}",
                        "Batch Processing",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    string skippedNote = skippedRows > 0
                        ? $" Skipped {skippedRows} empty row(s)."
                        : string.Empty;
                    MessageBox.Show(
                        $"Batch complete. Generated {items.Count} file(s).{skippedNote}",
                        "Batch Processing",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
        }

        private void GenerateAndSaveBatchItem(BatchItem item, BatchRenderSettings settings, string outputFolder, bool outputSvg)
        {
            string extension = outputSvg ? ".svg" : ".png";
            string outputPath = Path.Combine(outputFolder, item.FileName + extension);

            if (outputSvg)
            {
                string svg = _qrService.GenerateCustomQrCodeSvg(
                    item.Data,
                    settings.Foreground,
                    settings.Foreground2,
                    settings.UseGradient,
                    settings.Background,
                    settings.Finder,
                    settings.InnerEye,
                    settings.ModuleShape,
                    settings.EyeShape,
                    settings.LogoPath,
                    settings.LogoSizePercent,
                    settings.AddLogoBackground);

                File.WriteAllText(outputPath, svg);
                return;
            }

            using Bitmap? bitmap = _qrService.GenerateCustomQrCode(
                item.Data,
                settings.Foreground,
                settings.Foreground2,
                settings.UseGradient,
                settings.Background,
                settings.Finder,
                settings.InnerEye,
                settings.ModuleShape,
                settings.EyeShape,
                settings.LogoPath,
                settings.LogoSizePercent,
                settings.AddLogoBackground);

            if (bitmap == null)
                throw new InvalidOperationException("QR generation returned an empty image.");

            bitmap.Save(outputPath, ImageFormat.Png);
        }

        private BatchRenderSettings CaptureBatchRenderSettings()
        {
            return new BatchRenderSettings
            {
                Foreground = FgColorRow.SelectedColor,
                Foreground2 = FgColor2Row.SelectedColor,
                Background = BgColorRow.SelectedColor,
                Finder = FinderColorRow.SelectedColor,
                InnerEye = InnerEyeColorRow.SelectedColor,
                UseGradient = UseGradientCheckBox.IsChecked == true,
                ModuleShape = (ShapeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Squares",
                EyeShape = (EyeShapeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Square",
                LogoSizePercent = (int)LogoSizeSlider.Value,
                AddLogoBackground = LogoBackgroundCheckBox.IsChecked == true,
                LogoPath = LogoPathTextBox.Text
            };
        }

        private List<BatchItem> BuildBatchItems(string csvPath, out int skippedRows)
        {
            string csvText = File.ReadAllText(csvPath);
            List<string[]> records = ParseCsvRecords(csvText);
            skippedRows = 0;

            if (records.Count == 0)
                return new List<BatchItem>();

            string[] firstRow = records[0];
            bool hasHeader = LooksLikeHeader(firstRow);
            int maxColumns = records.Max(r => r.Length);
            string[] headers = hasHeader ? firstRow : BuildDefaultHeaders(maxColumns);
            int dataIndex = FindColumnIndex(headers, DataHeaderNames);
            int nameIndex = FindColumnIndex(headers, FileHeaderNames);
            int startRow = hasHeader ? 1 : 0;

            if (dataIndex < 0)
                dataIndex = 0;

            var items = new List<BatchItem>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = startRow; i < records.Count; i++)
            {
                string[] row = records[i];
                string data = GetField(row, dataIndex).Trim();
                if (string.IsNullOrWhiteSpace(data))
                {
                    skippedRows++;
                    continue;
                }

                string rawName = nameIndex >= 0 ? GetField(row, nameIndex).Trim() : string.Empty;
                string fileName = BuildUniqueFileName(rawName, i + 1, usedNames);
                items.Add(new BatchItem(data, fileName, i + 1));
            }

            return items;
        }

        private static List<string[]> ParseCsvRecords(string csvText)
        {
            var records = new List<string[]>();
            var fields = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < csvText.Length; i++)
            {
                char c = csvText[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        bool escaped = i + 1 < csvText.Length && csvText[i + 1] == '"';
                        if (escaped)
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }

                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                    continue;
                }

                if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                    continue;
                }

                if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < csvText.Length && csvText[i + 1] == '\n')
                        i++;

                    fields.Add(field.ToString());
                    field.Clear();

                    if (fields.Count > 1 || !string.IsNullOrWhiteSpace(fields[0]))
                        records.Add(fields.ToArray());

                    fields.Clear();
                    continue;
                }

                field.Append(c);
            }

            fields.Add(field.ToString());
            if (fields.Count > 1 || !string.IsNullOrWhiteSpace(fields[0]))
                records.Add(fields.ToArray());

            return records;
        }

        private static bool LooksLikeHeader(IReadOnlyList<string> row)
        {
            foreach (string cell in row)
            {
                string normalized = NormalizeHeader(cell);
                if (DataHeaderNames.Contains(normalized) || FileHeaderNames.Contains(normalized))
                    return true;
            }

            return false;
        }

        private static int FindColumnIndex(IReadOnlyList<string> headers, HashSet<string> candidates)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                string normalized = NormalizeHeader(headers[i]);
                if (candidates.Contains(normalized))
                    return i;
            }

            return -1;
        }

        private static string[] BuildDefaultHeaders(int count)
        {
            int columns = Math.Max(1, count);
            var headers = new string[columns];
            for (int i = 0; i < columns; i++)
                headers[i] = $"Column{i + 1}";
            return headers;
        }

        private static string GetField(IReadOnlyList<string> row, int index)
        {
            if (index < 0 || index >= row.Count)
                return string.Empty;

            return row[index] ?? string.Empty;
        }

        private static string BuildUniqueFileName(string rawName, int rowNumber, HashSet<string> usedNames)
        {
            string baseName = string.IsNullOrWhiteSpace(rawName)
                ? $"qr_{rowNumber:0000}"
                : SanitizeFileName(rawName);

            if (string.IsNullOrWhiteSpace(baseName))
                baseName = $"qr_{rowNumber:0000}";

            string candidate = baseName;
            int suffix = 2;

            while (!usedNames.Add(candidate))
            {
                candidate = $"{baseName}-{suffix}";
                suffix++;
            }

            return candidate;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Length);

            foreach (char c in name)
                builder.Append(invalid.Contains(c) ? '_' : c);

            return builder.ToString().Trim();
        }

        private static string NormalizeHeader(string? header)
        {
            if (string.IsNullOrWhiteSpace(header))
                return string.Empty;

            string normalized = header.Trim();
            normalized = normalized.Replace(" ", string.Empty, StringComparison.Ordinal);
            normalized = normalized.Replace("_", string.Empty, StringComparison.Ordinal);
            normalized = normalized.Replace("-", string.Empty, StringComparison.Ordinal);
            return normalized;
        }

        private void UpdateBatchProgress(BatchProgress progress)
        {
            if (progress.Total <= 0)
                return;

            double percent = progress.Completed * 100d / progress.Total;
            BatchProgressBar.Value = percent;

            if (progress.Errors > 0)
                BatchStatusTextBlock.Text = $"{progress.Completed} / {progress.Total} (Errors: {progress.Errors})";
            else
                BatchStatusTextBlock.Text = $"{progress.Completed} / {progress.Total}";
        }

        private void SetBatchUiState(bool isRunning)
        {
            BatchStartButton.IsEnabled = !isRunning;
            BatchCancelButton.IsEnabled = isRunning;
            BatchBrowseCsvButton.IsEnabled = !isRunning;
            BatchBrowseOutputButton.IsEnabled = !isRunning;
            BatchOutputFormatComboBox.IsEnabled = !isRunning;
            BatchCsvPathTextBox.IsEnabled = !isRunning;
            BatchOutputFolderTextBox.IsEnabled = !isRunning;
        }

        // ── Presets ─────────────────────────────────────────────────────────

        private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PresetLoadButton.IsEnabled = PresetComboBox.SelectedItem is PresetInfo;
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
