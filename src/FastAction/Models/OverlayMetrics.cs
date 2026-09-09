namespace FastAction.Models;

/// <summary>DIP sizing for the overlay so the window hugs the tile grid.</summary>
public static class OverlayMetrics
{
    public const int TileSpacing = 8;
    public const int GridPadX = 12;
    public const int GridPadTop = 12;
    public const int GridPadBottom = 16;

    public static int TilesWidth(int columns, int tileSize, int spacing = TileSpacing) =>
        (Math.Max(1, columns) * tileSize) + (Math.Max(0, columns - 1) * spacing);

    public static int TilesHeight(int rows, int tileSize, int spacing = TileSpacing) =>
        (Math.Max(1, rows) * tileSize) + (Math.Max(0, rows - 1) * spacing);

    public static int GridWidth(int columns, int tileSize, int spacing = TileSpacing) =>
        TilesWidth(columns, tileSize, spacing) + (GridPadX * 2);

    public static int WindowWidth(int columns, int tileSize, int chromeWidth, int extraWidth = 0) =>
        Math.Max(GridWidth(columns, tileSize), Math.Max(chromeWidth, extraWidth));
}
