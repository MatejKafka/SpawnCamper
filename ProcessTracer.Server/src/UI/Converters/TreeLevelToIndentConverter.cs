using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ProcessTracer.Server.UI.Converters;

public class TreeLevelToIndentConverter : IValueConverter {
    public double IndentSize {get; set;} = 16d;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        var indent = value is TreeViewItem item ? GetDepth(item) * IndentSize : 0d;

        if (targetType == typeof(Thickness)) {
            return new Thickness(indent, 0, 0, 0);
        }

        if (targetType == typeof(GridLength)) {
            return new GridLength(indent, GridUnitType.Pixel);
        }

        return indent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
        return DependencyProperty.UnsetValue;
    }

    private static int GetDepth(TreeViewItem item) {
        var depth = 0;
        var parent = ItemsControl.ItemsControlFromItemContainer(item);
        while (parent is TreeViewItem parentItem) {
            depth++;
            parent = ItemsControl.ItemsControlFromItemContainer(parentItem);
        }
        return depth;
    }
}