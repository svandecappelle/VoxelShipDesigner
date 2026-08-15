using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ShipDesign.App.Converters;

public sealed class ColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Color c ? new SolidColorBrush(c) : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
