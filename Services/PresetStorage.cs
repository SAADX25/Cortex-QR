using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using CortexQR.Models;

namespace CortexQR.Services
{
    public sealed class PresetStorage
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public string PresetsDirectory { get; }

        public PresetStorage(string? presetsDirectory = null)
        {
            PresetsDirectory = string.IsNullOrWhiteSpace(presetsDirectory)
                ? GetDefaultPresetsDirectory()
                : presetsDirectory;
        }

        public static string GetDefaultPresetsDirectory()
        {
            string basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(basePath, "CortexQR", "Presets");
        }

        public string SavePreset(QrStylePreset preset)
        {
            if (preset == null)
                throw new ArgumentNullException(nameof(preset));

            if (string.IsNullOrWhiteSpace(preset.Name))
                throw new ArgumentException("Preset name is required.", nameof(preset));

            Directory.CreateDirectory(PresetsDirectory);

            preset.Version = QrStylePreset.CurrentVersion;
            if (preset.CreatedUtc == default)
                preset.CreatedUtc = DateTimeOffset.UtcNow;
            preset.UpdatedUtc = DateTimeOffset.UtcNow;

            string fileName = SanitizeFileName(preset.Name);
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("Preset name produces an empty file name.", nameof(preset));

            string filePath = Path.Combine(PresetsDirectory, fileName + ".json");
            string json = JsonSerializer.Serialize(preset, JsonOptions);
            File.WriteAllText(filePath, json, Encoding.UTF8);
            return filePath;
        }

        public string GetPresetFilePath(string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName))
                throw new ArgumentException("Preset name is required.", nameof(presetName));

            Directory.CreateDirectory(PresetsDirectory);

            string fileName = SanitizeFileName(presetName);
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("Preset name produces an empty file name.", nameof(presetName));

            return Path.Combine(PresetsDirectory, fileName + ".json");
        }

        public bool PresetExists(string presetName)
        {
            string filePath = GetPresetFilePath(presetName);
            return File.Exists(filePath);
        }

        public QrStylePreset LoadPreset(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Preset file path is required.", nameof(filePath));

            string json = File.ReadAllText(filePath, Encoding.UTF8);
            QrStylePreset? preset = JsonSerializer.Deserialize<QrStylePreset>(json, JsonOptions);

            if (preset == null)
                throw new InvalidOperationException("Preset file could not be parsed.");

            if (string.IsNullOrWhiteSpace(preset.Name))
                preset.Name = Path.GetFileNameWithoutExtension(filePath);

            return preset;
        }

        public IReadOnlyList<PresetInfo> ListPresets()
        {
            Directory.CreateDirectory(PresetsDirectory);

            var list = new List<PresetInfo>();
            foreach (string file in Directory.EnumerateFiles(PresetsDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    QrStylePreset preset = LoadPreset(file);
                    string displayName = string.IsNullOrWhiteSpace(preset.Name)
                        ? Path.GetFileNameWithoutExtension(file)
                        : preset.Name;
                    list.Add(new PresetInfo(displayName, file, preset.UpdatedUtc));
                }
                catch
                {
                    DateTime lastWrite = File.GetLastWriteTimeUtc(file);
                    var lastWriteOffset = new DateTimeOffset(lastWrite, TimeSpan.Zero);
                    list.Add(new PresetInfo(Path.GetFileNameWithoutExtension(file), file, lastWriteOffset));
                }
            }

            return list
                .OrderByDescending(info => info.UpdatedUtc)
                .ToList();
        }

        public void DeletePreset(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Preset file path is required.", nameof(filePath));

            if (File.Exists(filePath))
                File.Delete(filePath);
        }

        private static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Length);

            foreach (char c in name)
                builder.Append(invalid.Contains(c) ? '_' : c);

            return builder.ToString().Trim();
        }
    }

    public sealed record PresetInfo(string Name, string FilePath, DateTimeOffset UpdatedUtc);
}
