using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MaterialDesignThemes.Wpf;

namespace CortexQR.Services
{
    public class MessageDialogService : IMessageDialogService
    {
        public void ShowInfo(string message, string title)
        {
            Show(message, title, DialogTone.Info);
        }

        public void ShowWarning(string message, string title)
        {
            Show(message, title, DialogTone.Warning);
        }

        public void ShowError(string message, string title)
        {
            Show(message, title, DialogTone.Error);
        }

        private static void Show(string message, string title, DialogTone tone)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                ShowFallback(message, title, tone);
                return;
            }

            if (dispatcher.CheckAccess())
            {
                _ = ShowDialogAsync(message, title, tone);
                return;
            }

            dispatcher.BeginInvoke(() => _ = ShowDialogAsync(message, title, tone));
        }

        private static async Task ShowDialogAsync(string message, string title, DialogTone tone)
        {
            try
            {
                await DialogHost.Show(CreateDialogContent(message, title, tone), "RootDialog");
            }
            catch
            {
                ShowFallback(message, title, tone);
            }
        }

        private static Border CreateDialogContent(string message, string title, DialogTone tone)
        {
            Color accent = tone switch
            {
                DialogTone.Warning => Hex("#F59E0B"),
                DialogTone.Error => Hex("#F43F5E"),
                _ => Hex("#0A84FF"),
            };

            string symbol = tone switch
            {
                DialogTone.Warning => "!",
                DialogTone.Error => "x",
                _ => "i",
            };

            var icon = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(0x22, accent.R, accent.G, accent.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = symbol,
                    Foreground = new SolidColorBrush(accent),
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            var titleText = new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Hex("#EEF4FF")),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var messageText = new TextBlock
            {
                Text = message,
                Foreground = new SolidColorBrush(Hex("#9DBBE2")),
                FontSize = 13,
                LineHeight = 19,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0),
            };

            var copy = new StackPanel();
            copy.Children.Add(titleText);
            copy.Children.Add(messageText);

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(icon, 0);
            Grid.SetColumn(copy, 1);
            icon.Margin = new Thickness(0, 0, 12, 0);
            header.Children.Add(icon);
            header.Children.Add(copy);

            var okButton = new Button
            {
                Content = "OK",
                Width = 92,
                Height = 36,
                Padding = new Thickness(16, 0, 16, 0),
                Style = Application.Current.TryFindResource("PrimaryButtonStyle") as Style,
                IsDefault = true,
                Command = DialogHost.CloseDialogCommand,
            };

            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0),
            };
            footer.Children.Add(okButton);

            var panel = new StackPanel();
            panel.Children.Add(header);
            panel.Children.Add(footer);

            return new Border
            {
                Width = 360,
                Padding = new Thickness(18),
                Background = new SolidColorBrush(Hex("#0F1520")),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Effect = new DropShadowEffect
                {
                    Color = Hex("#000000"),
                    BlurRadius = 28,
                    ShadowDepth = 12,
                    Opacity = 0.42,
                },
                Child = panel,
            };
        }

        private static void ShowFallback(string message, string title, DialogTone tone)
        {
            MessageBoxImage image = tone switch
            {
                DialogTone.Warning => MessageBoxImage.Warning,
                DialogTone.Error => MessageBoxImage.Error,
                _ => MessageBoxImage.Information,
            };

            MessageBox.Show(message, title, MessageBoxButton.OK, image);
        }

        private static Color Hex(string value) =>
            (Color)ColorConverter.ConvertFromString(value)!;

        private enum DialogTone
        {
            Info,
            Warning,
            Error,
        }
    }
}
