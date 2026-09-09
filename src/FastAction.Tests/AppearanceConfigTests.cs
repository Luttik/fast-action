using FastAction.Models;
using Xunit;

namespace FastAction.Tests;

public sealed class AppearanceConfigTests
{
    [Theory]
    [InlineData(null, "system")]
    [InlineData("Dark", "dark")]
    [InlineData("LIGHT", "light")]
    [InlineData("whatever", "system")]
    public void NormalizeTheme(string? input, string expected)
    {
        Assert.Equal(expected, AppearanceConfig.NormalizeTheme(input));
    }

    [Theory]
    [InlineData(72, 72)]
    [InlineData(112, 112)]
    [InlineData(88, 88)]
    [InlineData(100, 88)]
    public void NormalizeTileSize(int input, int expected)
    {
        Assert.Equal(expected, AppearanceConfig.NormalizeTileSize(input));
    }

    [Theory]
    [InlineData(10, 20)]
    [InlineData(80, 80)]
    [InlineData(100, 100)]
    [InlineData(140, 100)]
    public void NormalizeOpacity(int input, int expected)
    {
        Assert.Equal(expected, AppearanceConfig.NormalizeOpacity(input));
    }

    [Theory]
    [InlineData(null, "standard")]
    [InlineData("SOFT", "soft")]
    [InlineData("thin", "standard")]
    public void NormalizeAcrylicBlur(string? input, string expected)
    {
        Assert.Equal(expected, AppearanceConfig.NormalizeAcrylicBlur(input));
    }
}
