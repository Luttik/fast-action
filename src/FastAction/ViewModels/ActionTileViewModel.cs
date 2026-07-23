using FastAction.Models;
using Microsoft.UI.Xaml.Media;

namespace FastAction.ViewModels;

public sealed class ActionTileViewModel
{
    public required string Key { get; init; }

    public string? Name { get; init; }

    public ActionItemConfig? Item { get; init; }

    public bool IsEmpty => Item is null;

    public ImageSource? Icon { get; set; }
}

public sealed class GridRowViewModel
{
    public required IReadOnlyList<ActionTileViewModel> Tiles { get; init; }

    public double LeadingOffset { get; init; }
}
