using FastAction.Models;
using Xunit;

namespace FastAction.Tests;

public sealed class LucidePaletteTests
{
    [Theory]
    [InlineData(null, "auto")]
    [InlineData("Blue", "blue")]
    [InlineData("nope", "auto")]
    public void Normalize(string? input, string expected)
    {
        Assert.Equal(expected, LucidePalette.Normalize(input));
    }

    [Fact]
    public void ResolveHex_AutoFollowsTheme()
    {
        Assert.Equal("#F2F2F2", LucidePalette.ResolveHex("auto", darkTheme: true));
        Assert.Equal("#2B2B2B", LucidePalette.ResolveHex("auto", darkTheme: false));
        Assert.Equal("#60CDFF", LucidePalette.ResolveHex("blue", darkTheme: true));
    }

    [Fact]
    public void TryParseRgb_ReadsHex()
    {
        Assert.True(LucidePalette.TryParseRgb("#60CDFF", out var r, out var g, out var b));
        Assert.Equal(0x60, r);
        Assert.Equal(0xCD, g);
        Assert.Equal(0xFF, b);
    }
}
