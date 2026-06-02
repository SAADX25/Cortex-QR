using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CortexQR.Services;

namespace CortexQR.ViewModels
{
    public class BatchProcessingViewModel : ObservableObject
    {
        private static readonly HashSet<string> DataHeaderNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "data", "payload", "text", "content", "url", "qr", "qrdata", "qrcode", "qrcontent", "message"
        };

        private static readonly HashSet<string> FileHeaderNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "filename", "file", "name", "title", "id"
        };

        private readonly QrGenerationService _qrService;
        private readonly IFileDialogService _fileDialogService;
        private readonly IMessageDialogService _messageService;
        private readonly Func<BatchRenderSettings> _getRenderSettings;
        private readonly AsyncRelayCommand _startBatchCommand;
        private readonly RelayCommand _cancelBatchCommand;

        private CancellationTokenSource? _cts;
        private string _csvPath = string.Empty;
        private string _outputFolder = string.Empty;
        private string _selectedOutputFormat = "PNG";
        private double _progress;
        private string _statusText = "Idle";
        private bool _isRunning;

        public BatchProcessingViewModel(
            QrGenerationService qrService,
            IFileDialogService fileDialogService,
            IMessageDialogService messageService,
            Func<BatchRenderSettings> getRenderSettings)
        {
            _qrService = qrService;
            _fileDialogService = fileDialogService;
            _messageService = messageService;
            _getRenderSettings = getRenderSettings;

            _startBatchCommand = new AsyncRelayCommand(StartBatchAsync, CanStartBatch);
            _cancelBatchCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsRunning);

            BrowseCsvCommand = new RelayCommand(_ => BrowseCsv());
            BrowseOutputFolderCommand = new RelayCommand(_ => BrowseOutputFolder());
        }

        public ICommand StartBatchCommand => _startBatchCommand;
        public ICommand CancelBatchCommand => _cancelBatchCommand;
        public ICommand BrowseCsvCommand { get; }
        public ICommand BrowseOutputFolderCommand { get; }

        public IReadOnlyList<string> OutputFormats { get; } = new[] { "PNG", "SVG" };

        public string CsvPath
        {
            get => _csvPath;
            set => SetProperty(ref _csvPath, value);
        }

        public string OutputFolder
        {
            get => _outputFolder;
            set => SetProperty(ref _outputFolder, value);
        }

        public string SelectedOutputFormat
        {
            get => _selectedOutputFormat;
            set
            {
                if (SetProperty(ref _selectedOutputFormat, value))
                    _startBatchCommand.RaiseCanExecuteChanged();
            }
        }

        public double Progress
        {
            get => _progress;
            private set => SetProperty(ref _progress, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    _startBatchCommand.RaiseCanExecuteChanged();
                    _cancelBatchCommand.RaiseCanExecuteChanged();
                }
            }
        }

        private void BrowseCsv()
        {
            string? path = _fileDialogService.OpenFile("CSV Files|*.csv|All Files|*.*", "Select CSV file");
            if (!string.IsNullOrWhiteSpace(path))
                CsvPath = path;
        }

        private void BrowseOutputFolder()
        {
            string? folder = _fileDialogService.OpenFolder("Select output folder for QR images");
            if (!string.IsNullOrWhiteSpace(folder))
                OutputFolder = folder;
        }

        private bool CanStartBatch() => !IsRunning;

        private async Task StartBatchAsync()
        {
            if (IsRunning)
                return;

            if (string.IsNullOrWhiteSpace(CsvPath) || !File.Exists(CsvPath))
            {
                _messageService.ShowWarning("Please select a valid CSV file.", "Batch Processing");
                return;
            }

            if (string.IsNullOrWhiteSpace(OutputFolder))
            {
                _messageService.ShowWarning("Please select an output folder.", "Batch Processing");
                return;
            }

            try
            {
                Directory.CreateDirectory(OutputFolder);
            }
            catch (Exception ex)
            {
                _messageService.ShowError($"Unable to create output folder: {ex.Message}", "Batch Processing");
                return;
            }

            List<BatchItem> items;
            int skippedRows;
            try
            {
                items = BuildBatchItems(CsvPath, out skippedRows);
            }
            catch (Exception ex)
            {
                _messageService.ShowError($"CSV parse error: {ex.Message}", "Batch Processing");
                return;
            }

            if (items.Count == 0)
            {
                _messageService.ShowWarning("No valid data rows found in the CSV.", "Batch Processing");
                return;
            }

            BatchRenderSettings settings = _getRenderSettings();
            bool outputSvg = string.Equals(SelectedOutputFormat, "SVG", StringComparison.OrdinalIgnoreCase);

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            IsRunning = true;
            Progress = 0;
            StatusText = $"0 / {items.Count}";

            var errors = new List<string>();
            int completed = 0;
            int errorCount = 0;
            int maxConcurrency = Math.Max(1, Math.Min(Environment.ProcessorCount, 4));
            using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var tasks = new List<Task>(items.Count);
            IProgress<BatchProgress> progress = new Progress<BatchProgress>(UpdateProgress);

            try
            {
                foreach (BatchItem item in items)
                {
                    await semaphore.WaitAsync(token).ConfigureAwait(false);

                    tasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            GenerateAndSaveBatchItem(item, settings, OutputFolder, outputSvg, token);
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

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                StatusText = "Batch cancelled.";
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                IsRunning = false;
            }

            if (token.IsCancellationRequested)
                return;

            if (errors.Count > 0)
            {
                string logPath = Path.Combine(OutputFolder, $"batch-errors-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                File.WriteAllLines(logPath, errors);
                _messageService.ShowWarning($"Batch complete with {errors.Count} error(s). See log: {logPath}", "Batch Processing");
            }
            else
            {
                string skippedNote = skippedRows > 0 ? $" Skipped {skippedRows} empty row(s)." : string.Empty;
                _messageService.ShowInfo($"Batch complete. Generated {items.Count} file(s).{skippedNote}", "Batch Processing");
            }
        }

        private void UpdateProgress(BatchProgress progress)
        {
            if (progress.Total <= 0)
                return;

            Progress = progress.Completed * 100d / progress.Total;
            StatusText = progress.Errors > 0
                ? $"{progress.Completed} / {progress.Total} (Errors: {progress.Errors})"
                : $"{progress.Completed} / {progress.Total}";
        }

        private void GenerateAndSaveBatchItem(BatchItem item, BatchRenderSettings settings, string outputFolder, bool outputSvg, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

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

            using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            bitmap.Save(fileStream, ImageFormat.Png);
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

        private sealed record BatchItem(string Data, string FileName, int RowNumber);

        private sealed record BatchProgress(int Completed, int Total, int Errors, string? CurrentFile);
    }
}
