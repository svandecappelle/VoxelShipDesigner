using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ShipDesign.Core.Procedural;

namespace ShipDesign.App.ViewModels;

/// <summary>
/// One universe's silhouettes, plus whether its fold is open.
///
/// The fold state lives here rather than on <see cref="SilhouetteGroup"/> because it is a property
/// of this window, not of the ship catalogue: Core has no notion of a sidebar, and a record whose
/// members are init-only cannot carry a two-way binding anyway.
///
/// Groups open by default. With three universes and eight ships the folds cost nothing and hiding
/// half the catalogue behind a click would be worse than the flat list they replace; they earn
/// their keep once there are enough variants that the column no longer fits.
/// </summary>
public sealed class SilhouetteGroupViewModel : INotifyPropertyChanged
{
    private bool _isExpanded = true;

    public SilhouetteGroupViewModel(SilhouetteGroup group)
    {
        Universe = group.Universe;
        Silhouettes = group.Silhouettes;
    }

    public string Universe { get; }
    public IReadOnlyList<ShipSilhouette> Silhouettes { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
