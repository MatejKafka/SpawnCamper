using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using SpawnCamper.Server.ViewModels;

namespace SpawnCamper.Server.Converters;

public class EnvironmentDifferencesToFlowDocumentConverter : IValueConverter {
    public Brush? AddedForeground {get; init;}
    public Brush? AddedBackground {get; init;}
    public Brush? RemovedForeground {get; init;}
    public Brush? RemovedBackground {get; init;}

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        var document = new FlowDocument {
            PagePadding = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextAlignment = TextAlignment.Left,
        };

        if (value is IEnumerable<EnvironmentVariableDifference> diffs) {
            foreach (var d in diffs) {
                var (foreground, background) = d.Kind switch {
                    EnvironmentVariableDiffKind.Added => (AddedForeground, AddedBackground),
                    EnvironmentVariableDiffKind.Removed => (RemovedForeground, RemovedBackground),
                    _ => (null, null),
                };

                var paragraph = new Paragraph {
                    Margin = new Thickness(0),
                    Padding = new Thickness(0),
                    TextAlignment = TextAlignment.Left,
                };

                if (foreground != null) paragraph.Foreground = foreground;
                if (background != null) paragraph.Background = background;

                paragraph.Inlines.Add(new Run($"{d.Key}={d.Value ?? ""}"));
                document.Blocks.Add(paragraph);
            }
        }

        return document;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}