using FastAction.Models;
using Xunit;

namespace FastAction.Tests;

public sealed class KeyboardLayoutTests
{
    [Fact]
    public void Default_IsFourByFourStartingAtOne()
    {
        var layout = KeyboardLayout.Default;

        Assert.Equal("1", layout.StartKey);
        Assert.Equal(
            [
                ["1", "2", "3", "4"],
                ["Q", "W", "E", "R"],
                ["A", "S", "D", "F"],
                ["Z", "X", "C", "V"],
            ],
            layout.Rows.Select(row => row.ToArray()).ToArray());
    }

    [Fact]
    public void Build_ThreeByThreeStartingAtQ()
    {
        var layout = KeyboardLayout.Build("q", 3, 3);

        Assert.Equal("Q", layout.StartKey);
        Assert.Equal(
            [
                ["Q", "W", "E"],
                ["A", "S", "D"],
                ["Z", "X", "C"],
            ],
            layout.Rows.Select(row => row.ToArray()).ToArray());
        Assert.True(layout.IsValidKey("s"));
        Assert.False(layout.IsValidKey("1"));
        Assert.False(layout.IsValidKey("R"));
    }

    [Fact]
    public void Build_ClampsSizeAndFallsBackForUnknownOrigin()
    {
        var layout = KeyboardLayout.Build("not-a-key", 0, 99);

        Assert.Equal("1", layout.StartKey);
        Assert.Equal(1, layout.RequestedColumns);
        Assert.Equal(4, layout.RequestedRows);
        Assert.Equal(["1"], layout.Rows[0]);
    }

    [Fact]
    public void Build_StopsWhenLowerRowsDoNotReachTheOriginColumn()
    {
        var layout = KeyboardLayout.Build("P", 3, 2);

        Assert.Equal([["P"]], layout.Rows.Select(row => row.ToArray()).ToArray());
    }

    [Fact]
    public void Build_HomeRowOriginUsesRemainingLetterRows()
    {
        var layout = KeyboardLayout.Build("A", 4, 4);

        Assert.Equal(
            [
                ["A", "S", "D", "F"],
                ["Z", "X", "C", "V"],
            ],
            layout.Rows.Select(row => row.ToArray()).ToArray());
        Assert.Equal(2, layout.RowCount);
        Assert.Equal(4, layout.RequestedRows);
    }

    [Fact]
    public void ContainsPhysicalCell_MatchesTheActiveSlice()
    {
        var layout = KeyboardLayout.Build("Q", 3, 3);

        Assert.True(layout.ContainsPhysicalCell(1, 0)); // Q
        Assert.True(layout.ContainsPhysicalCell(3, 2)); // C
        Assert.False(layout.ContainsPhysicalCell(0, 0)); // 1
        Assert.False(layout.ContainsPhysicalCell(1, 3)); // R
    }

    [Fact]
    public void From_UsesLayoutConfig()
    {
        var layout = KeyboardLayout.From(new LayoutConfig
        {
            StartKey = "2",
            Columns = 3,
            Rows = 3,
        });

        Assert.Equal(
            [
                ["2", "3", "4"],
                ["W", "E", "R"],
                ["S", "D", "F"],
            ],
            layout.Rows.Select(row => row.ToArray()).ToArray());
    }

    [Theory]
    [InlineData("q", "Q")]
    [InlineData("1", "1")]
    [InlineData("  a ", "A")]
    public void NormalizeKey_TrimsAndUppercasesLetters(string input, string expected)
    {
        Assert.Equal(expected, KeyboardLayout.NormalizeKey(input));
    }
}
