using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace CortexQR.Views
{
    public partial class ColorRow : UserControl
    {
        // ── Dependency Properties ─────────────────────────────────────────────

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(ColorRow),
                new PropertyMetadata("Color"));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public static readonly DependencyProperty InitialColorProperty =
            DependencyProperty.Register(nameof(InitialColor), typeof(string), typeof(ColorRow),
                new PropertyMetadata("#FF000000", OnInitialColorChanged));

        public string InitialColor
        {
            get => (string)GetValue(InitialColorProperty);
            set => SetValue(InitialColorProperty, value);
        }

        public static readonly DependencyProperty SelectedColorProperty =
            DependencyProperty.Register(nameof(SelectedColor), typeof(Color), typeof(ColorRow),
                new PropertyMetadata(Colors.Black, OnSelectedColorChanged));

        public Color SelectedColor
        {
            get => (Color)GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        public static readonly DependencyProperty SelectedBrushProperty =
            DependencyProperty.Register(nameof(SelectedBrush), typeof(SolidColorBrush), typeof(ColorRow),
                new PropertyMetadata(new SolidColorBrush(Colors.Black)));

        public SolidColorBrush SelectedBrush
        {
            get => (SolidColorBrush)GetValue(SelectedBrushProperty);
            private set => SetValue(SelectedBrushProperty, value);
        }

        // ── Events ────────────────────────────────────────────────────────────
        public event EventHandler? ColorChanged;

        // ── Constructor ───────────────────────────────────────────────────────
        public ColorRow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                // Apply InitialColor on first load
                if (TryParseColor(InitialColor, out Color c))
                    ApplyColor(c, fireEvent: false);
                else
                    ApplyColor(Colors.Black, fireEvent: false);
            };
        }

        // ── DP Callbacks ──────────────────────────────────────────────────────
        private static void OnInitialColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ColorRow cr && cr.IsLoaded)
                if (TryParseColor((string)e.NewValue, out Color c))
                    cr.ApplyColor(c, fireEvent: false);
        }

        private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // Keeps SelectedBrush and HexBox in sync when SelectedColor is set externally
            if (d is ColorRow cr)
            {
                var newColor = (Color)e.NewValue;
                cr.SelectedBrush = new SolidColorBrush(newColor);
                cr.UpdateHexBox(newColor);
            }
        }

        // ── Internal ──────────────────────────────────────────────────────────
        private void ApplyColor(Color c, bool fireEvent = true)
        {
            SelectedColor = c;
            SelectedBrush = new SolidColorBrush(c);
            UpdateHexBox(c);
            if (fireEvent) ColorChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateHexBox(Color c)
        {
            bool hasAlpha = c.A < 255;
            HexBox.Text = hasAlpha
                ? $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}"
                : $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        // ── Swatch Button ─────────────────────────────────────────────────────
        private async void SwatchBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new ColorPickerPopup
            {
                SelectedColor = SelectedColor
            };

            picker.ColorApplied += (_, color) =>
            {
                ApplyColor(color);
                DialogHost.CloseDialogCommand.Execute(null, picker);
            };

            picker.Cancelled += (_, _) =>
            {
                DialogHost.CloseDialogCommand.Execute(null, picker);
            };

            await DialogHost.Show(picker, "RootDialog");
        }

        // ── Hex TextBox ───────────────────────────────────────────────────────
        private void HexBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ApplyHexInput();
        }

        private void HexBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyHexInput();
        }

        private void ApplyHexInput()
        {
            if (TryParseColor(HexBox.Text, out Color c))
                ApplyColor(c);
        }

        // ── Color Picker Popup Callbacks ──────────────────────────────────────
        // ── Helpers ───────────────────────────────────────────────────────────
        private static bool TryParseColor(string? input, out Color color)
        {
            color = Colors.Black;
            if (string.IsNullOrWhiteSpace(input)) return false;
            try
            {
                var raw = input.Trim();
                // Ensure it starts with #
                if (!raw.StartsWith('#')) raw = '#' + raw;
                color = (Color)ColorConverter.ConvertFromString(raw);
                return true;
            }
            catch { return false; }
        }
    }
}
