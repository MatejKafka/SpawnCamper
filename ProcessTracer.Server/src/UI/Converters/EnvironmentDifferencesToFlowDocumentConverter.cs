using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using ProcessTracer.Server.UI.ViewModels;

namespace ProcessTracer.Server.UI.Converters;

public class EnvironmentDifferencesToFlowDocumentConverter : IValueConverter {
    public Brush? AddedForeground { get; set; }
    public Brush? AddedBackground { get; set; }
    public Brush? RemovedForeground { get; set; }
    public Brush? RemovedBackground { get; set; }
    public Brush? DefaultForeground { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        var document = new FlowDocument {
            PagePadding = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextAlignment = TextAlignment.Left
        };

        if (value is IEnumerable<EnvironmentVariableDifference> differences) {
            foreach (var difference in differences) {
                var paragraph = new Paragraph {
                    Margin = new Thickness(0),
                    Padding = new Thickness(0),
                    TextAlignment = TextAlignment.Left
                };

                Brush? foreground = null;
                if (difference.IsAdded) {
                    foreground = AddedForeground ?? DefaultForeground;
                    if (AddedBackground != null) {
                        paragraph.Background = AddedBackground;
                    }
                } else if (difference.IsRemoved) {
                    foreground = RemovedForeground ?? DefaultForeground;
                    if (RemovedBackground != null) {
                        paragraph.Background = RemovedBackground;
                    }
                } else {
                    foreground = DefaultForeground;
                }

                var resolvedForeground = foreground ?? DefaultForeground ?? Brushes.Black;

                var prefixTextBlock = new TextBlock {
                    Text = difference.Prefix,
                    FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 6, 0),
                    Foreground = resolvedForeground,
                    Background = paragraph.Background,
                    Focusable = false,
                    IsHitTestVisible = false
                };

                var prefixContainer = new InlineUIContainer(prefixTextBlock) {
                    BaselineAlignment = BaselineAlignment.Center
                };

                var valueRun = new Run(difference.ValueText);
                valueRun.Foreground = resolvedForeground;

                paragraph.Inlines.Add(prefixContainer);
                paragraph.Inlines.Add(valueRun);
                document.Blocks.Add(paragraph);
            }
        }

        return document;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
