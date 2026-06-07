using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace CortexQR.Helpers;

public static class ComboBoxAssist
{
    public static readonly DependencyProperty PopupPlacementProperty =
        DependencyProperty.RegisterAttached(
            "PopupPlacement",
            typeof(PlacementMode),
            typeof(ComboBoxAssist),
            new FrameworkPropertyMetadata(PlacementMode.Bottom));

    public static PlacementMode GetPopupPlacement(ComboBox comboBox)
    {
        return (PlacementMode)comboBox.GetValue(PopupPlacementProperty);
    }

    public static void SetPopupPlacement(ComboBox comboBox, PlacementMode value)
    {
        comboBox.SetValue(PopupPlacementProperty, value);
    }
}
