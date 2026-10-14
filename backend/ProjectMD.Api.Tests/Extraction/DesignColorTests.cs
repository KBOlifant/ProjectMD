using ProjectMD.Api.Services.Extraction.Design;

namespace ProjectMD.Api.Tests.Extraction;

// Tests for turning CSS colour text into "#rrggbb", and for the shade-merging key.
public class DesignColorTests
{
    // Every supported colour notation lands on the same lower-case hex form.
    [Theory]
    [InlineData("rgb(59, 130, 246)", "#3b82f6")]
    [InlineData("rgba(59, 130, 246, 0.8)", "#3b82f6")]
    [InlineData("rgb(59 130 246 / 50%)", "#3b82f6")]
    [InlineData("rgb(100%, 0%, 0%)", "#ff0000")]
    [InlineData("#3B82F6", "#3b82f6")]
    [InlineData("#fff", "#ffffff")]
    [InlineData("#0af", "#00aaff")]
    [InlineData("#3b82f6ff", "#3b82f6")]
    [InlineData("hsl(217, 91%, 60%)", "#3c83f6")]
    [InlineData("hsl(0 100% 50%)", "#ff0000")]
    [InlineData("hsla(120, 100%, 25%, 1)", "#008000")]
    [InlineData("RebeccaPurple", "#663399")]
    [InlineData("white", "#ffffff")]
    [InlineData("  rgb( 0 , 0 , 0 ) ", "#000000")]
    public void Normalize_returns_lowercase_hex(string css, string expected)
    {
        Assert.Equal(expected, ColorParser.Normalize(css));
    }

    // Transparent and unreadable values give null instead of a colour.
    [Theory]
    [InlineData("transparent")]
    [InlineData("rgba(0, 0, 0, 0)")]
    [InlineData("rgb(0 0 0 / 0%)")]
    [InlineData("#00000000")]
    [InlineData("hsla(0, 0%, 0%, 0)")]
    [InlineData("var(--brand)")]
    [InlineData("currentcolor")]
    [InlineData("#12")]
    [InlineData("#gggggg")]
    [InlineData("rgb(1, 2)")]
    [InlineData("notacolor")]
    [InlineData("")]
    [InlineData(null)]
    public void Normalize_returns_null_for_transparent_or_unknown(string? css)
    {
        Assert.Null(ColorParser.Normalize(css));
    }

    // Channels outside 0-255 are clamped, not rejected.
    [Fact]
    public void Normalize_clamps_out_of_range_channels()
    {
        Assert.Equal("#ff0000", ColorParser.Normalize("rgb(300, -5, 0)"));
    }

    // Near-identical shades share one merge key; clearly different colours do not.
    [Fact]
    public void QuantizedKey_groups_close_shades_only()
    {
        Assert.Equal(ColorParser.QuantizedKey("#3b82f6"), ColorParser.QuantizedKey("#3c83f6"));
        Assert.NotEqual(ColorParser.QuantizedKey("#3b82f6"), ColorParser.QuantizedKey("#3b8af6"));
        Assert.Equal("#ffffff", ColorParser.QuantizedKey("#ffffff"));
        Assert.Equal("#000000", ColorParser.QuantizedKey("#000000"));
    }

    // Grey has no saturation, pure red has full saturation.
    [Fact]
    public void Saturation_separates_grey_from_vivid_colours()
    {
        Assert.Equal(0, ColorParser.Saturation("#808080"));
        Assert.Equal(1, ColorParser.Saturation("#ff0000"));
        Assert.Equal(0, ColorParser.Saturation("#000000"));
    }
}
