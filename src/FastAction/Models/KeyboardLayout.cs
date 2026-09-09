namespace FastAction.Models;

/// <summary>
/// QWERTY-aligned action grid. The overlay is a rectangular slice of the physical
/// key map, anchored at <see cref="StartKey"/> (top-left) with the requested size.
/// <code>
/// Default startKey=1, 4×4:
/// 1 2 3 4
/// Q W E R
/// A S D F
/// Z X C V
/// </code>
/// </summary>
public sealed class KeyboardLayout
{
    public const int MinSize = 1;
    public const int MaxRows = 4;
    public const int MaxColumns = 10;

    /// <summary>Physical QWERTY rows used as the source map (US layout, no punctuation).</summary>
    public static readonly string[][] PhysicalRows =
    [
        ["1", "2", "3", "4", "5", "6", "7", "8", "9", "0"],
        ["Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P"],
        ["A", "S", "D", "F", "G", "H", "J", "K", "L"],
        ["Z", "X", "C", "V", "B", "N", "M"],
    ];

    public static readonly (int Rows, int Columns, string Label)[] SizePresets =
    [
        (3, 3, "3 × 3"),
        (3, 4, "3 × 4"),
        (4, 4, "4 × 4"),
        (4, 5, "4 × 5"),
        (4, 6, "4 × 6"),
        (4, 10, "Full width"),
    ];

    public static readonly string[] CommonOrigins = ["1", "Q", "A", "Z"];

    public static KeyboardLayout Default { get; } = Build("1", 4, 4);

    public KeyboardLayout(
        IReadOnlyList<IReadOnlyList<string>> rows,
        string startKey,
        int requestedColumns,
        int requestedRows)
    {
        Rows = rows;
        StartKey = startKey;
        RequestedColumns = requestedColumns;
        RequestedRows = requestedRows;
        AllKeys = rows.SelectMany(row => row).ToArray();
    }

    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }

    public IReadOnlyList<string> AllKeys { get; }

    public string StartKey { get; }

    public int RequestedColumns { get; }

    public int RequestedRows { get; }

    public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(row => row.Count);

    public int RowCount => Rows.Count;

    public static KeyboardLayout From(LayoutConfig? layout)
    {
        layout ??= new LayoutConfig();
        return Build(layout.StartKey, layout.Columns, layout.Rows);
    }

    public static KeyboardLayout Build(string? startKey, int columns, int rows)
    {
        columns = Math.Clamp(columns, MinSize, MaxColumns);
        rows = Math.Clamp(rows, MinSize, MaxRows);

        var origin = NormalizeKey(startKey ?? string.Empty);
        if (!TryFindPhysicalKey(origin, out var originRow, out var originCol))
        {
            origin = "1";
            originRow = 0;
            originCol = 0;
        }

        var builtRows = new List<IReadOnlyList<string>>();
        for (var r = 0; r < rows; r++)
        {
            var sourceRow = originRow + r;
            if (sourceRow >= PhysicalRows.Length)
            {
                break;
            }

            var physical = PhysicalRows[sourceRow];
            if (originCol >= physical.Length)
            {
                break;
            }

            var take = Math.Min(columns, physical.Length - originCol);
            if (take <= 0)
            {
                break;
            }

            builtRows.Add(physical.Skip(originCol).Take(take).ToArray());
        }

        if (builtRows.Count == 0)
        {
            return Default;
        }

        return new KeyboardLayout(builtRows, origin, columns, rows);
    }

    public bool IsValidKey(string key) =>
        AllKeys.Contains(NormalizeKey(key), StringComparer.OrdinalIgnoreCase);

    public static bool IsPhysicalKey(string key) =>
        TryFindPhysicalKey(NormalizeKey(key), out _, out _);

    public static bool TryFindPhysicalKey(string key, out int row, out int column)
    {
        var normalized = NormalizeKey(key);
        for (var r = 0; r < PhysicalRows.Length; r++)
        {
            for (var c = 0; c < PhysicalRows[r].Length; c++)
            {
                if (string.Equals(PhysicalRows[r][c], normalized, StringComparison.OrdinalIgnoreCase))
                {
                    row = r;
                    column = c;
                    return true;
                }
            }
        }

        row = 0;
        column = 0;
        return false;
    }

    public bool ContainsPhysicalCell(int physicalRow, int physicalColumn)
    {
        if (!TryFindPhysicalKey(StartKey, out var originRow, out var originCol))
        {
            return false;
        }

        var localRow = physicalRow - originRow;
        var localCol = physicalColumn - originCol;
        if (localRow < 0 || localRow >= Rows.Count)
        {
            return false;
        }

        var row = Rows[localRow];
        return localCol >= 0 && localCol < row.Count;
    }

    public static string NormalizeKey(string key)
    {
        var trimmed = (key ?? string.Empty).Trim();
        if (trimmed.Length == 1 && char.IsDigit(trimmed[0]))
        {
            return trimmed;
        }

        return trimmed.ToUpperInvariant();
    }
}
