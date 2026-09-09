using FastAction.Models;
using Xunit;

namespace FastAction.Tests;

public sealed class OverlayMetricsTests
{
    [Fact]
    public void WindowWidth_GrowsWithColumnCount()
    {
        const int tile = 88;
        const int chrome = 220;
        var three = OverlayMetrics.WindowWidth(3, tile, chrome);
        var four = OverlayMetrics.WindowWidth(4, tile, chrome);
        var ten = OverlayMetrics.WindowWidth(10, tile, chrome);

        Assert.True(four > three);
        Assert.True(ten > four);
        Assert.Equal(OverlayMetrics.GridWidth(3, tile), three);
        Assert.Equal(OverlayMetrics.GridWidth(4, tile), four);
    }

    [Fact]
    public void WindowWidth_UsesChromeWhenGridIsNarrower()
    {
        var width = OverlayMetrics.WindowWidth(columns: 1, tileSize: 72, chromeWidth: 240);
        Assert.Equal(240, width);
    }

    [Fact]
    public void WindowWidth_TwoColumnsHugsTilesInsteadOfOldFloor()
    {
        var width = OverlayMetrics.WindowWidth(columns: 2, tileSize: 88, chromeWidth: 180);
        Assert.Equal(OverlayMetrics.GridWidth(2, 88), width);
        Assert.True(width < 320);
    }
}
