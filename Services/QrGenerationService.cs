using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using QRCoder;

namespace CortexQR.Services
{
    public class QrGenerationService
    {
        public Bitmap? GenerateCustomQrCode(
            string data,
            System.Windows.Media.Color fgMediaColor,
            System.Windows.Media.Color fg2MediaColor,
            bool useGradient,
            System.Windows.Media.Color bgMediaColor,
            System.Windows.Media.Color finderMediaColor,
            System.Windows.Media.Color innerEyeMediaColor,
            string moduleShape,
            string eyeShape,
            string logoPath,
            int logoSizePercent,
            bool addLogoBackground)
        {
            if (string.IsNullOrWhiteSpace(data))
                return null;

            Color fgColor = ToDrawingColor(fgMediaColor);
            Color fg2Color = ToDrawingColor(fg2MediaColor);
            Color bgColor = ToDrawingColor(bgMediaColor);
            Color finderColor = ToDrawingColor(finderMediaColor);
            Color innerEyeColor = ToDrawingColor(innerEyeMediaColor);

            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(data, QRCodeGenerator.ECCLevel.H))
            {
                return RenderCustomQrCode(
                    qrCodeData,
                    20,
                    fgColor,
                    fg2Color,
                    useGradient,
                    bgColor,
                    finderColor,
                    innerEyeColor,
                    moduleShape,
                    eyeShape,
                    logoPath,
                    logoSizePercent,
                    addLogoBackground);
            }
        }

        public string GenerateCustomQrCodeSvg(
            string data,
            System.Windows.Media.Color fgMediaColor,
            System.Windows.Media.Color fg2MediaColor,
            bool useGradient,
            System.Windows.Media.Color bgMediaColor,
            System.Windows.Media.Color finderMediaColor,
            System.Windows.Media.Color innerEyeMediaColor,
            string moduleShape,
            string eyeShape,
            string logoPath,
            int logoSizePercent,
            bool addLogoBackground)
        {
            if (string.IsNullOrWhiteSpace(data))
                return string.Empty;

            Color fgColor = ToDrawingColor(fgMediaColor);
            Color fg2Color = ToDrawingColor(fg2MediaColor);
            Color bgColor = ToDrawingColor(bgMediaColor);
            Color finderColor = ToDrawingColor(finderMediaColor);
            Color innerEyeColor = ToDrawingColor(innerEyeMediaColor);

            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(data, QRCodeGenerator.ECCLevel.H))
            {
                return RenderCustomQrCodeSvg(
                    qrCodeData,
                    20,
                    fgColor,
                    fg2Color,
                    useGradient,
                    bgColor,
                    finderColor,
                    innerEyeColor,
                    moduleShape,
                    eyeShape,
                    logoPath,
                    logoSizePercent,
                    addLogoBackground);
            }
        }

        private Bitmap RenderCustomQrCode(
            QRCodeData qrCodeData,
            int pixelsPerModule,
            Color fgColor,
            Color fg2Color,
            bool useGradient,
            Color bgColor,
            Color finderColor,
            Color innerEyeColor,
            string moduleShape,
            string eyeShape,
            string logoPath,
            int logoSizePercent,
            bool addLogoBackground)
        {
            int numModules = qrCodeData.ModuleMatrix.Count;
            int size = numModules * pixelsPerModule;
            int quietZone = 4;
            int topLeftEyeX = quietZone;
            int topLeftEyeY = quietZone;
            int topRightEyeX = numModules - quietZone - 7;
            int topRightEyeY = quietZone;
            int bottomLeftEyeX = quietZone;
            int bottomLeftEyeY = numModules - quietZone - 7;
            string normalizedModuleShape = NormalizeShape(moduleShape);
            string normalizedEyeShape = NormalizeShape(eyeShape);

            Bitmap? logo = null;
            RectangleF logoRect = RectangleF.Empty;
            RectangleF logoCutoutBounds = RectangleF.Empty;

            if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
            {
                logo = new Bitmap(logoPath);

                float logoDestWidth = size * (logoSizePercent / 100f);
                float logoDestHeight = logoDestWidth;
                float aspect = (float)logo.Width / logo.Height;

                if (aspect > 1f)
                    logoDestHeight = logoDestWidth / aspect;
                else
                    logoDestWidth = logoDestHeight * aspect;

                float logoX = (size - logoDestWidth) / 2f;
                float logoY = (size - logoDestHeight) / 2f;
                logoRect = new RectangleF(logoX, logoY, logoDestWidth, logoDestHeight);

                float cutoutPadding = addLogoBackground ? pixelsPerModule * 1.25f : 0f;
                float cutoutDiameter = Math.Max(logoDestWidth, logoDestHeight) + cutoutPadding * 2f;
                logoCutoutBounds = new RectangleF(
                    (size - cutoutDiameter) / 2f,
                    (size - cutoutDiameter) / 2f,
                    cutoutDiameter,
                    cutoutDiameter);
            }

            Bitmap bitmap = new Bitmap(size, size);
            try
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;

                    using (SolidBrush bgBrush = new SolidBrush(bgColor))
                    {
                        graphics.FillRectangle(bgBrush, 0, 0, size, size);
                    }

                    using (Brush fgBrush = CreateForegroundBrush(size, fgColor, fg2Color, useGradient))
                    {
                        for (int y = 0; y < numModules; y++)
                        {
                            for (int x = 0; x < numModules; x++)
                            {
                                if (IsFinderPatternZone(x, y, numModules, quietZone))
                                    continue;

                                if (!qrCodeData.ModuleMatrix[y][x])
                                    continue;

                                float rectX = x * pixelsPerModule;
                                float rectY = y * pixelsPerModule;

                                if (addLogoBackground &&
                                    !logoCutoutBounds.IsEmpty &&
                                    IsModuleCenterInsideEllipse(rectX, rectY, pixelsPerModule, logoCutoutBounds))
                                {
                                    continue;
                                }

                                DrawModule(graphics, fgBrush, rectX, rectY, pixelsPerModule, normalizedModuleShape);
                            }
                        }
                    }

                    using (SolidBrush finderBrush = new SolidBrush(finderColor))
                    using (SolidBrush innerEyeBrush = new SolidBrush(innerEyeColor))
                    using (SolidBrush bgBrush = new SolidBrush(bgColor))
                    {
                        ClearFinderEyeZone(graphics, bgBrush, topLeftEyeX, topLeftEyeY, pixelsPerModule);
                        ClearFinderEyeZone(graphics, bgBrush, topRightEyeX, topRightEyeY, pixelsPerModule);
                        ClearFinderEyeZone(graphics, bgBrush, bottomLeftEyeX, bottomLeftEyeY, pixelsPerModule);

                        DrawFinderEye(graphics, finderBrush, innerEyeBrush, bgBrush, topLeftEyeX, topLeftEyeY, pixelsPerModule, normalizedEyeShape);
                        DrawFinderEye(graphics, finderBrush, innerEyeBrush, bgBrush, topRightEyeX, topRightEyeY, pixelsPerModule, normalizedEyeShape);
                        DrawFinderEye(graphics, finderBrush, innerEyeBrush, bgBrush, bottomLeftEyeX, bottomLeftEyeY, pixelsPerModule, normalizedEyeShape);
                    }

                    if (logo != null)
                    {
                        if (addLogoBackground)
                        {
                            using (SolidBrush bgBrush = new SolidBrush(bgColor))
                            {
                                graphics.FillEllipse(bgBrush, logoCutoutBounds);
                            }
                        }

                        graphics.DrawImage(logo, logoRect);
                    }
                }
            }
            finally
            {
                logo?.Dispose();
            }

            return bitmap;
        }

        private string RenderCustomQrCodeSvg(
            QRCodeData qrCodeData,
            int pixelsPerModule,
            Color fgColor,
            Color fg2Color,
            bool useGradient,
            Color bgColor,
            Color finderColor,
            Color innerEyeColor,
            string moduleShape,
            string eyeShape,
            string logoPath,
            int logoSizePercent,
            bool addLogoBackground)
        {
            int numModules = qrCodeData.ModuleMatrix.Count;
            int size = numModules * pixelsPerModule;
            int quietZone = 4;
            int topLeftEyeX = quietZone;
            int topLeftEyeY = quietZone;
            int topRightEyeX = numModules - quietZone - 7;
            int topRightEyeY = quietZone;
            int bottomLeftEyeX = quietZone;
            int bottomLeftEyeY = numModules - quietZone - 7;
            string normalizedModuleShape = NormalizeShape(moduleShape);
            string normalizedEyeShape = NormalizeShape(eyeShape);
            RectangleF logoRect = RectangleF.Empty;
            RectangleF logoCutoutBounds = RectangleF.Empty;
            string? logoDataUri = null;

            if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
            {
                using Bitmap logo = new Bitmap(logoPath);

                float logoDestWidth = size * (logoSizePercent / 100f);
                float logoDestHeight = logoDestWidth;
                float aspect = (float)logo.Width / logo.Height;

                if (aspect > 1f)
                    logoDestHeight = logoDestWidth / aspect;
                else
                    logoDestWidth = logoDestHeight * aspect;

                float logoX = (size - logoDestWidth) / 2f;
                float logoY = (size - logoDestHeight) / 2f;
                logoRect = new RectangleF(logoX, logoY, logoDestWidth, logoDestHeight);

                float cutoutPadding = addLogoBackground ? pixelsPerModule * 1.25f : 0f;
                float cutoutDiameter = Math.Max(logoDestWidth, logoDestHeight) + cutoutPadding * 2f;
                logoCutoutBounds = new RectangleF(
                    (size - cutoutDiameter) / 2f,
                    (size - cutoutDiameter) / 2f,
                    cutoutDiameter,
                    cutoutDiameter);

                logoDataUri = BuildImageDataUri(logoPath);
            }

            string dataFill = useGradient
                ? "url(#dataGradient)"
                : ToSvgColor(fgColor);

            var svg = new StringBuilder();
            svg.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
            svg.AppendLine(string.Create(CultureInfo.InvariantCulture, $"""<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 {size} {size}" shape-rendering="geometricPrecision">"""));

            if (useGradient)
            {
                svg.AppendLine("  <defs>");
                svg.AppendLine(string.Create(CultureInfo.InvariantCulture, $"""    <linearGradient id="dataGradient" x1="0" y1="0" x2="{size}" y2="{size}" gradientUnits="userSpaceOnUse">"""));
                svg.AppendLine(string.Create(CultureInfo.InvariantCulture, $"""      <stop offset="0%" stop-color="{ToSvgColor(fgColor)}" stop-opacity="{ToSvgOpacity(fgColor)}" />"""));
                svg.AppendLine(string.Create(CultureInfo.InvariantCulture, $"""      <stop offset="100%" stop-color="{ToSvgColor(fg2Color)}" stop-opacity="{ToSvgOpacity(fg2Color)}" />"""));
                svg.AppendLine("    </linearGradient>");
                svg.AppendLine("  </defs>");
            }

            AppendSvgRect(svg, 0, 0, size, size, bgColor);

            for (int y = 0; y < numModules; y++)
            {
                for (int x = 0; x < numModules; x++)
                {
                    if (IsFinderPatternZone(x, y, numModules, quietZone))
                        continue;

                    if (!qrCodeData.ModuleMatrix[y][x])
                        continue;

                    float rectX = x * pixelsPerModule;
                    float rectY = y * pixelsPerModule;

                    if (addLogoBackground &&
                        !logoCutoutBounds.IsEmpty &&
                        IsModuleCenterInsideEllipse(rectX, rectY, pixelsPerModule, logoCutoutBounds))
                    {
                        continue;
                    }

                    AppendSvgModule(svg, rectX, rectY, pixelsPerModule, normalizedModuleShape, dataFill, useGradient ? null : fgColor);
                }
            }

            AppendSvgRect(svg, topLeftEyeX * pixelsPerModule, topLeftEyeY * pixelsPerModule, pixelsPerModule * 7, pixelsPerModule * 7, bgColor);
            AppendSvgRect(svg, topRightEyeX * pixelsPerModule, topRightEyeY * pixelsPerModule, pixelsPerModule * 7, pixelsPerModule * 7, bgColor);
            AppendSvgRect(svg, bottomLeftEyeX * pixelsPerModule, bottomLeftEyeY * pixelsPerModule, pixelsPerModule * 7, pixelsPerModule * 7, bgColor);

            AppendSvgFinderEye(svg, finderColor, innerEyeColor, bgColor, topLeftEyeX, topLeftEyeY, pixelsPerModule, normalizedEyeShape);
            AppendSvgFinderEye(svg, finderColor, innerEyeColor, bgColor, topRightEyeX, topRightEyeY, pixelsPerModule, normalizedEyeShape);
            AppendSvgFinderEye(svg, finderColor, innerEyeColor, bgColor, bottomLeftEyeX, bottomLeftEyeY, pixelsPerModule, normalizedEyeShape);

            if (logoDataUri != null)
            {
                if (addLogoBackground)
                {
                    AppendSvgEllipse(
                        svg,
                        logoCutoutBounds.Left + logoCutoutBounds.Width / 2f,
                        logoCutoutBounds.Top + logoCutoutBounds.Height / 2f,
                        logoCutoutBounds.Width / 2f,
                        logoCutoutBounds.Height / 2f,
                        bgColor);
                }

                svg.AppendLine(string.Create(CultureInfo.InvariantCulture, $"""  <image href="{XmlEscape(logoDataUri)}" x="{F(logoRect.X)}" y="{F(logoRect.Y)}" width="{F(logoRect.Width)}" height="{F(logoRect.Height)}" preserveAspectRatio="xMidYMid meet" />"""));
            }

            svg.AppendLine("</svg>");
            return svg.ToString();
        }

        private static void DrawModule(Graphics graphics, Brush brush, float x, float y, int pixelsPerModule, string moduleShape)
        {
            if (moduleShape is "dots" or "dots/circles")
            {
                const float dotScale = 0.92f;
                float diameter = pixelsPerModule * dotScale;
                float padding = (pixelsPerModule - diameter) / 2f;
                graphics.FillEllipse(brush, x + padding, y + padding, diameter, diameter);
                return;
            }

            if (moduleShape is "roundedsquares" or "rounded")
            {
                const float moduleScale = 0.94f;
                float moduleSize = pixelsPerModule * moduleScale;
                float padding = (pixelsPerModule - moduleSize) / 2f;
                RectangleF rect = new RectangleF(x + padding, y + padding, moduleSize, moduleSize);

                using (GraphicsPath path = CreateRoundedRectanglePath(rect, moduleSize * 0.32f))
                {
                    graphics.FillPath(brush, path);
                }
                return;
            }

            graphics.FillRectangle(brush, x, y, pixelsPerModule, pixelsPerModule);
        }

        private static Brush CreateForegroundBrush(int size, Color fgColor, Color fg2Color, bool useGradient)
        {
            if (!useGradient)
                return new SolidBrush(fgColor);

            return new LinearGradientBrush(
                new PointF(0, 0),
                new PointF(size, size),
                fgColor,
                fg2Color);
        }

        private static void AppendSvgModule(
            StringBuilder svg,
            float x,
            float y,
            int pixelsPerModule,
            string moduleShape,
            string fill,
            Color? solidColor)
        {
            if (moduleShape is "dots" or "dots/circles")
            {
                const float dotScale = 0.92f;
                float diameter = pixelsPerModule * dotScale;
                float padding = (pixelsPerModule - diameter) / 2f;
                AppendSvgCircle(
                    svg,
                    x + padding + diameter / 2f,
                    y + padding + diameter / 2f,
                    diameter / 2f,
                    fill,
                    solidColor);
                return;
            }

            if (moduleShape is "roundedsquares" or "rounded")
            {
                const float moduleScale = 0.94f;
                float moduleSize = pixelsPerModule * moduleScale;
                float padding = (pixelsPerModule - moduleSize) / 2f;
                float radius = moduleSize * 0.32f;
                svg.AppendLine(string.Create(CultureInfo.InvariantCulture, $"""  <rect x="{F(x + padding)}" y="{F(y + padding)}" width="{F(moduleSize)}" height="{F(moduleSize)}" rx="{F(radius)}" ry="{F(radius)}" {SvgFill(fill, solidColor)} />"""));
                return;
            }

            svg.AppendLine(string.Create(CultureInfo.InvariantCulture, $"""  <rect x="{F(x)}" y="{F(y)}" width="{pixelsPerModule}" height="{pixelsPerModule}" {SvgFill(fill, solidColor)} />"""));
        }

        private static void AppendSvgFinderEye(
            StringBuilder svg,
            Color outerColor,
            Color innerColor,
            Color bgColor,
            int moduleX,
            int moduleY,
            int pixelsPerModule,
            string eyeShape)
        {
            float x = moduleX * pixelsPerModule;
            float y = moduleY * pixelsPerModule;
            RectangleF outer = new RectangleF(x, y, pixelsPerModule * 7f, pixelsPerModule * 7f);
            RectangleF middle = new RectangleF(x + pixelsPerModule, y + pixelsPerModule, pixelsPerModule * 5f, pixelsPerModule * 5f);
            RectangleF inner = new RectangleF(x + pixelsPerModule * 2f, y + pixelsPerModule * 2f, pixelsPerModule * 3f, pixelsPerModule * 3f);

            AppendSvgEyeShape(svg, outer, eyeShape, pixelsPerModule * 1.25f, outerColor);
            AppendSvgEyeShape(svg, middle, eyeShape, pixelsPerModule, bgColor);
            AppendSvgEyeShape(svg, inner, eyeShape, pixelsPerModule * 0.8f, innerColor);
        }

        private static void AppendSvgEyeShape(StringBuilder svg, RectangleF rect, string eyeShape, float radius, Color color)
        {
            if (eyeShape == "circular")
            {
                AppendSvgEllipse(
                    svg,
                    rect.Left + rect.Width / 2f,
                    rect.Top + rect.Height / 2f,
                    rect.Width / 2f,
                    rect.Height / 2f,
                    color);
                return;
            }

            if (eyeShape == "rounded")
            {
                svg.AppendLine(string.Create(CultureInfo.InvariantCulture, $"""  <path d="{CreateRoundedRectangleSvgPath(rect, radius)}" {SvgFill(ToSvgColor(color), color)} />"""));
                return;
            }

            AppendSvgRect(svg, rect.X, rect.Y, rect.Width, rect.Height, color);
        }

        private static void AppendSvgRect(StringBuilder svg, float x, float y, float width, float height, Color color)
        {
            svg.AppendLine(string.Create(CultureInfo.InvariantCulture, $"""  <rect x="{F(x)}" y="{F(y)}" width="{F(width)}" height="{F(height)}" {SvgFill(ToSvgColor(color), color)} />"""));
        }

        private static void AppendSvgCircle(StringBuilder svg, float cx, float cy, float r, string fill, Color? solidColor)
        {
            svg.AppendLine(string.Create(CultureInfo.InvariantCulture, $"""  <circle cx="{F(cx)}" cy="{F(cy)}" r="{F(r)}" {SvgFill(fill, solidColor)} />"""));
        }

        private static void AppendSvgEllipse(StringBuilder svg, float cx, float cy, float rx, float ry, Color color)
        {
            svg.AppendLine(string.Create(CultureInfo.InvariantCulture, $"""  <ellipse cx="{F(cx)}" cy="{F(cy)}" rx="{F(rx)}" ry="{F(ry)}" {SvgFill(ToSvgColor(color), color)} />"""));
        }

        private static void DrawFinderEye(
            Graphics graphics,
            Brush outerBrush,
            Brush innerBrush,
            Brush backgroundBrush,
            int moduleX,
            int moduleY,
            int pixelsPerModule,
            string eyeShape)
        {
            float x = moduleX * pixelsPerModule;
            float y = moduleY * pixelsPerModule;
            RectangleF outer = new RectangleF(x, y, pixelsPerModule * 7f, pixelsPerModule * 7f);
            RectangleF middle = new RectangleF(x + pixelsPerModule, y + pixelsPerModule, pixelsPerModule * 5f, pixelsPerModule * 5f);
            RectangleF inner = new RectangleF(x + pixelsPerModule * 2f, y + pixelsPerModule * 2f, pixelsPerModule * 3f, pixelsPerModule * 3f);

            FillEyeShape(graphics, outerBrush, outer, eyeShape, pixelsPerModule * 1.25f);
            FillEyeShape(graphics, backgroundBrush, middle, eyeShape, pixelsPerModule);
            FillEyeShape(graphics, innerBrush, inner, eyeShape, pixelsPerModule * 0.8f);
        }

        private static void ClearFinderEyeZone(Graphics graphics, Brush backgroundBrush, int moduleX, int moduleY, int pixelsPerModule)
        {
            graphics.FillRectangle(
                backgroundBrush,
                moduleX * pixelsPerModule,
                moduleY * pixelsPerModule,
                pixelsPerModule * 7,
                pixelsPerModule * 7);
        }

        private static void FillEyeShape(Graphics graphics, Brush brush, RectangleF rect, string eyeShape, float radius)
        {
            if (eyeShape == "circular")
            {
                graphics.FillEllipse(brush, rect);
                return;
            }

            if (eyeShape == "rounded")
            {
                using (GraphicsPath path = CreateRoundedRectanglePath(rect, radius))
                {
                    graphics.FillPath(brush, path);
                }
                return;
            }

            graphics.FillRectangle(brush, rect);
        }

        private static GraphicsPath CreateRoundedRectanglePath(RectangleF rect, float radius)
        {
            float diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
            RectangleF arc = new RectangleF(rect.Location, new SizeF(diameter, diameter));
            GraphicsPath path = new GraphicsPath();

            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();

            return path;
        }

        private static string CreateRoundedRectangleSvgPath(RectangleF rect, float radius)
        {
            float r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2f);
            float left = rect.Left;
            float top = rect.Top;
            float right = rect.Right;
            float bottom = rect.Bottom;

            return string.Create(
                CultureInfo.InvariantCulture,
                $"""M {F(left + r)} {F(top)} H {F(right - r)} A {F(r)} {F(r)} 0 0 1 {F(right)} {F(top + r)} V {F(bottom - r)} A {F(r)} {F(r)} 0 0 1 {F(right - r)} {F(bottom)} H {F(left + r)} A {F(r)} {F(r)} 0 0 1 {F(left)} {F(bottom - r)} V {F(top + r)} A {F(r)} {F(r)} 0 0 1 {F(left + r)} {F(top)} Z""");
        }

        private static string SvgFill(string fill, Color? solidColor)
        {
            if (solidColor is not Color color || color.A == 255)
                return string.Create(CultureInfo.InvariantCulture, $"""fill="{fill}" """);

            return string.Create(CultureInfo.InvariantCulture, $"""fill="{fill}" fill-opacity="{ToSvgOpacity(color)}" """);
        }

        private static string ToSvgColor(Color color)
        {
            return string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
        }

        private static string ToSvgOpacity(Color color)
        {
            return (color.A / 255d).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string F(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string BuildImageDataUri(string logoPath)
        {
            string mimeType = Path.GetExtension(logoPath).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".bmp" => "image/bmp",
                _ => "image/png"
            };

            return $"data:{mimeType};base64,{Convert.ToBase64String(File.ReadAllBytes(logoPath))}";
        }

        private static string XmlEscape(string value)
        {
            return value
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal);
        }

        private static bool IsFinderPatternZone(int x, int y, int numModules, int quietZone)
        {
            const int finderSize = 7;

            bool inTopLeft = x >= quietZone && x < quietZone + finderSize &&
                             y >= quietZone && y < quietZone + finderSize;

            bool inTopRight = x >= numModules - quietZone - finderSize && x < numModules - quietZone &&
                              y >= quietZone && y < quietZone + finderSize;

            bool inBottomLeft = x >= quietZone && x < quietZone + finderSize &&
                                y >= numModules - quietZone - finderSize && y < numModules - quietZone;

            return inTopLeft || inTopRight || inBottomLeft;
        }

        private static bool IsModuleCenterInsideEllipse(float moduleX, float moduleY, int moduleSize, RectangleF ellipseBounds)
        {
            float centerX = moduleX + moduleSize / 2f;
            float centerY = moduleY + moduleSize / 2f;
            float radiusX = ellipseBounds.Width / 2f;
            float radiusY = ellipseBounds.Height / 2f;

            if (radiusX <= 0 || radiusY <= 0)
                return false;

            float normalizedX = (centerX - (ellipseBounds.Left + radiusX)) / radiusX;
            float normalizedY = (centerY - (ellipseBounds.Top + radiusY)) / radiusY;
            return normalizedX * normalizedX + normalizedY * normalizedY <= 1f;
        }

        private static string NormalizeShape(string? shape)
        {
            return string.IsNullOrWhiteSpace(shape)
                ? string.Empty
                : shape.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        }

        private static Color ToDrawingColor(System.Windows.Media.Color color)
        {
            return Color.FromArgb(color.A, color.R, color.G, color.B);
        }
    }
}
