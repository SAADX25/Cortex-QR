using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using QRCoder;

namespace CortexQR.Services
{
    public class QrGenerationService
    {
        public Bitmap? GenerateCustomQrCode(
            string data,
            System.Windows.Media.Color fgMediaColor,
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

                    using (SolidBrush fgBrush = new SolidBrush(fgColor))
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
