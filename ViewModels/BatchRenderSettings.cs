using System.Windows.Media;

namespace CortexQR.ViewModels
{
    public record BatchRenderSettings(
        Color Foreground,
        Color Foreground2,
        Color Background,
        Color Finder,
        Color InnerEye,
        bool UseGradient,
        string ModuleShape,
        string EyeShape,
        string LogoPath,
        int LogoSizePercent,
        bool AddLogoBackground);
}
