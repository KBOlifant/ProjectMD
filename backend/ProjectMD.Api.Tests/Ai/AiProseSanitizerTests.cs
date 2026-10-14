using ProjectMD.Api.Services.Ai;

namespace ProjectMD.Api.Tests.Ai;

// Tests for AiProseSanitizer: the model's answer is treated as untrusted before it is cached or shown.
public class AiProseSanitizerTests
{
    // Empty answers give an empty result.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Empty_answer_gives_empty_prose(string? answer)
    {
        Assert.Equal(string.Empty, AiProseSanitizer.Clean(answer));
    }

    // A normal answer is kept, with headings pushed down to level 3 so the document structure holds.
    [Fact]
    public void Headings_are_demoted_to_level_three()
    {
        var clean = AiProseSanitizer.Clean("# Title\n## Summary\nText.\n#### Deep\n- bullet");

        Assert.Equal("### Title\n### Summary\nText.\n#### Deep\n- bullet", clean);
    }

    // HTML, scripts, comments and images are removed; links keep only their text.
    [Fact]
    public void Html_and_links_are_removed()
    {
        var clean = AiProseSanitizer.Clean(
            "Hello <script>alert(1)</script><b>world</b> <!-- hidden --> [click](javascript:alert(1)) ![x](http://evil/x.png) done");

        Assert.DoesNotContain("<", clean);
        Assert.DoesNotContain("javascript", clean);
        Assert.DoesNotContain("evil", clean);
        Assert.Contains("click", clean);
        Assert.Contains("world", clean);
    }

    // An answer wrapped in a code fence is unwrapped.
    [Fact]
    public void Wrapping_code_fence_is_removed()
    {
        var clean = AiProseSanitizer.Clean("```markdown\n## Summary\nAcme.\n```");

        Assert.Equal("### Summary\nAcme.", clean);
    }

    // Control characters go and long runs of blank lines collapse.
    [Fact]
    public void Control_characters_and_blank_runs_are_cleaned()
    {
        var clean = AiProseSanitizer.Clean("a\u0000b\r\n\r\n\r\n\r\nc");

        Assert.Equal("ab\n\nc", clean);
    }

    // The result is capped, at a whole line.
    [Fact]
    public void Very_long_answers_are_capped()
    {
        var answer = string.Join("\n", Enumerable.Repeat("- a fairly long bullet point with several words", 500));

        var clean = AiProseSanitizer.Clean(answer);

        Assert.True(clean.Length <= AiProseSanitizer.MaxChars);
        Assert.EndsWith("several words", clean);
    }
}
