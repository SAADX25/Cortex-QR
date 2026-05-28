using System;

namespace CortexQR.Models
{
    public sealed class QrStylePreset
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        public string Name { get; set; } = string.Empty;

        public string Foreground { get; set; } = "#FF000000";
        public string Foreground2 { get; set; } = "#FF3F51B5";
        public string Background { get; set; } = "#FFFFFFFF";
        public string Finder { get; set; } = "#FF000000";
        public string InnerEye { get; set; } = "#FF000000";

        public bool UseGradient { get; set; }
        public string ModuleShape { get; set; } = "Squares";
        public string EyeShape { get; set; } = "Square";

        public string LogoPath { get; set; } = string.Empty;
        public int LogoSizePercent { get; set; }
        public bool AddLogoBackground { get; set; }

        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
