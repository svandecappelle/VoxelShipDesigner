using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using ShipDesign.Core.Procedural;

namespace ShipDesign.App.Converters;

/// <summary>Maps the procedural generator's enum values to French labels for the segmented
/// button-group pickers, since WPF's default ListBox display just calls ToString().</summary>
public sealed class EnumLabelConverter : IValueConverter
{
    private static readonly Dictionary<object, string> Labels = new()
    {
        [HullClass.Fighter] = "Chasseur",
        [HullClass.Corvette] = "Corvette",
        [HullClass.Freighter] = "Cargo",
        [HullClass.Cruiser] = "Croiseur",

        [WingStyle.None] = "Aucune",
        [WingStyle.Swept] = "Flèche",
        [WingStyle.Delta] = "Delta",
        [WingStyle.TwinFin] = "Ailerons",

        [EngineStyle.Standard] = "Standard",
        [EngineStyle.Ring] = "Anneau",

        [CockpitStyle.Bubble] = "Bulle",
        [CockpitStyle.FlatCanopy] = "Verrière",
        [CockpitStyle.None] = "Aucun",

        [HullShape.Dart] = "Dard",
        [HullShape.Wedge] = "Coin",
        [HullShape.Spindle] = "Fuseau",
        [HullShape.Slab] = "Bloc",
        [HullShape.Hammerhead] = "Marteau",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && Labels.TryGetValue(value, out var label) ? label : value?.ToString() ?? "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
