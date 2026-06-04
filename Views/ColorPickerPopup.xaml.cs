using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CortexQR.Views
{
    public partial class ColorPickerPopup : UserControl
    {
        // ── Dependency Property ───────────────────────────────────────────────
        public static readonly DependencyProperty SelectedColorProperty =
            DependencyProperty.Register(nameof(SelectedColor), typeof(Color), typeof(ColorPickerPopup),
                new PropertyMetadata(Colors.Black, OnSelectedColorChanged));

        public Color SelectedColor
        {
            get => (Color)GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        // ── Events ────────────────────────────────────────────────────────────
        public event EventHandler<Color>? ColorApplied;
        public event EventHandler? Cancelled;

        // ── Internal State ────────────────────────────────────────────────────
        private double _hue = 0;        // 0-360
        private double _saturation = 1; // 0-1
        private double _value = 0.5;    // 0-1  (brightness/value)
        private bool _suppressUpdate = false;
        private bool _isDraggingSv = false;

        public ColorPickerPopup()
        {
            InitializeComponent();
            Loaded += (_, _) => SyncFromColor(SelectedColor);
        }

        // ── DP Callback ───────────────────────────────────────────────────────
        private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ColorPickerPopup cp && !cp._suppressUpdate)
                cp.SyncFromColor((Color)e.NewValue);
        }

        // ── Sync helpers ──────────────────────────────────────────────────────
        private void SyncFromColor(Color c)
        {
            _suppressUpdate = true;
            RgbToHsv(c.R, c.G, c.B, out _hue, out _saturation, out _value);
            HueSlider.Value = _hue;
            AlphaSlider.Value = c.A;
            UpdateHueGradient();
            UpdateSvCrosshair();
            UpdatePreview();
            UpdateHexBox();
            _suppressUpdate = false;
        }

        private void UpdateAll()
        {
            if (_suppressUpdate) return;
            var c = HsvToRgb(_hue, _saturation, _value);
            c.A = (byte)AlphaSlider.Value;
            _suppressUpdate = true;
            SelectedColor = c;
            UpdatePreview();
            UpdateHexBox();
            _suppressUpdate = false;
        }

        private void UpdatePreview()
        {
            if (PreviewEllipse == null || AlphaGradientRect == null || AlphaSlider == null) return;

            var c = HsvToRgb(_hue, _saturation, _value);
            c.A = (byte)AlphaSlider.Value;
            PreviewEllipse.Fill = new SolidColorBrush(c);

            // Alpha gradient on the alpha slider track
            var alphaStart = Color.FromArgb(0, c.R, c.G, c.B);
            var alphaEnd   = Color.FromArgb(255, c.R, c.G, c.B);
            AlphaGradientRect.Fill = new LinearGradientBrush(alphaStart, alphaEnd, 0);
        }

        private void UpdateHexBox()
        {
            if (AlphaSlider == null || HexTextBox == null) return;

            var c = HsvToRgb(_hue, _saturation, _value);
            c.A = (byte)AlphaSlider.Value;
            bool hasAlpha = c.A < 255;
            HexTextBox.Text = hasAlpha
                ? $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}"
                : $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        private void UpdateHueGradient()
        {
            if (HueGradientStop == null) return;
            // Set the right side of the SV canvas gradient to the pure hue color
            HueGradientStop.Color = HsvToRgb(_hue, 1.0, 1.0);
        }

        private void UpdateSvCrosshair()
        {
            // Position crosshair on the SV canvas
            if (SvCanvas.ActualWidth <= 0 || SvCanvas.ActualHeight <= 0) return;
            double cx = _saturation * SvCanvas.ActualWidth - 7;
            double cy = (1.0 - _value) * SvCanvas.ActualHeight - 7;
            System.Windows.Controls.Canvas.SetLeft(SvCrosshair, cx);
            System.Windows.Controls.Canvas.SetTop(SvCrosshair, cy);
        }

        // ── Title Bar Drag ────────────────────────────────────────────────────
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                Window.GetWindow(this)?.DragMove();
        }

        // ── SV Canvas Mouse ───────────────────────────────────────────────────
        private void SvCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSv = true;
            SvCanvas.CaptureMouse();
            UpdateSvFromMouse(e.GetPosition(SvCanvas));
        }

        private void SvCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingSv && e.LeftButton == MouseButtonState.Pressed)
                UpdateSvFromMouse(e.GetPosition(SvCanvas));
        }

        private void SvCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSv = false;
            SvCanvas.ReleaseMouseCapture();
        }

        private void UpdateSvFromMouse(Point p)
        {
            double w = SvCanvas.ActualWidth;
            double h = SvCanvas.ActualHeight;
            _saturation = Math.Clamp(p.X / w, 0, 1);
            _value       = Math.Clamp(1.0 - p.Y / h, 0, 1);
            UpdateSvCrosshair();
            UpdateAll();
        }

        // Override to re-position crosshair after layout pass
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            UpdateSvCrosshair();
        }

        // ── Hue Slider ────────────────────────────────────────────────────────
        private void HueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressUpdate) return;
            _hue = HueSlider.Value;
            UpdateHueGradient();
            UpdateAll();
        }

        // ── Alpha Slider ──────────────────────────────────────────────────────
        private void AlphaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressUpdate) return;
            UpdatePreview();
            UpdateHexBox();
        }

        // ── Hex TextBox ───────────────────────────────────────────────────────
        private void HexTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ApplyHexInput();
        }

        private void HexTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyHexInput();
        }

        private void ApplyHexInput()
        {
            if (_suppressUpdate) return;
            try
            {
                var raw = HexTextBox.Text.Trim().TrimStart('#');
                Color c;
                if (raw.Length == 6)
                    c = Color.FromRgb(
                        Convert.ToByte(raw[0..2], 16),
                        Convert.ToByte(raw[2..4], 16),
                        Convert.ToByte(raw[4..6], 16));
                else if (raw.Length == 8)
                    c = Color.FromArgb(
                        Convert.ToByte(raw[0..2], 16),
                        Convert.ToByte(raw[2..4], 16),
                        Convert.ToByte(raw[4..6], 16),
                        Convert.ToByte(raw[6..8], 16));
                else return;

                SyncFromColor(c);
            }
            catch { /* Invalid hex — ignore */ }
        }

        // ── Buttons ───────────────────────────────────────────────────────────
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var c = HsvToRgb(_hue, _saturation, _value);
            c.A = (byte)AlphaSlider.Value;
            SelectedColor = c;
            ColorApplied?.Invoke(this, c);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
        }

        // ── HSV / RGB Math ────────────────────────────────────────────────────
        private static Color HsvToRgb(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;
            double r = 0, g = 0, b = 0;
            if      (h < 60)  { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else              { r = c; b = x; }
            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        private static void RgbToHsv(byte r, byte g, byte b,
            out double h, out double s, out double v)
        {
            double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));
            double delta = max - min;
            v = max;
            s = max == 0 ? 0 : delta / max;
            if (delta == 0) { h = 0; return; }
            if      (max == rd) h = 60 * (((gd - bd) / delta) % 6);
            else if (max == gd) h = 60 * (((bd - rd) / delta) + 2);
            else                h = 60 * (((rd - gd) / delta) + 4);
            if (h < 0) h += 360;
        }
    }
}
