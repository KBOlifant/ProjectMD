using System.Text;
using ProjectMD.Api.Models;
using ProjectMD.Api.Services.Ai;
using ProjectMD.Api.Services.Extraction;

namespace ProjectMD.Api.Tests.Ai;

// Tests for PromptBuilder and TokenEstimator: the prompt stays small, keeps priority order and has
// an identical fixed prefix. No network and no model.
public class PromptBuilderTests
{
    private static readonly IReadOnlyList<ScanTech> Tech =
    [
        new ScanTech { Name = "Next.js", Highlighted = true },
        new ScanTech { Name = "Vercel", Highlighted = true },
    ];

    private static AiOptions Options(int maxInput = 3000) => new()
    {
        MaxInputTokens = maxInput,
        MaxOutputTokens = 700,
        FastModel = "fast-model",
        QualityModel = "quality-model",
    };

    // Total estimated input tokens of a request (system plus page facts).
    private static int InputTokens(LlmRequest request) =>
        TokenEstimator.Estimate(request.System) + TokenEstimator.Estimate(request.User);

    // The prompt never goes over Ai:MaxInputTokens, whatever the page or the budget.
    [Theory]
    [InlineData(500)]
    [InlineData(800)]
    [InlineData(1500)]
    [InlineData(3000)]
    [InlineData(8000)]
    public void Prompt_never_exceeds_max_input_tokens(int maxInput)
    {
        var request = new PromptBuilder().Build(Digests.Huge(), Tech, Options(maxInput));

        Assert.True(InputTokens(request) <= maxInput, $"{InputTokens(request)} > {maxInput}");
    }

    // Non-Latin text is counted generously, so it cannot sneak past the limit either.
    [Fact]
    public void Prompt_never_exceeds_the_limit_for_non_latin_text()
    {
        var digest = Digests.Huge("cjk", "日本語のテキストがここに入ります ");

        var request = new PromptBuilder().Build(digest, Tech, Options(1200));

        Assert.True(InputTokens(request) <= 1200);
        Assert.Contains("Title:", request.User);
    }

    // A small page is included in full (nothing is cut when it fits).
    [Fact]
    public void Small_page_is_included_in_full()
    {
        var request = new PromptBuilder().Build(Digests.Simple(), Tech, Options());

        Assert.Contains("Title: Acme Cloud", request.User);
        Assert.Contains("Description: Acme Cloud runs your apps close to your users.", request.User);
        Assert.Contains("Structured data: ", request.User);
        Assert.Contains("H2 Pricing", request.User);
        Assert.Contains("Technologies: Next.js, Vercel", request.User);
        Assert.Contains("Deploy in seconds.", request.User);
        Assert.Contains("- Pricing -> https://acme.example/pricing", request.User);
    }

    // Sections appear in priority order: title, description, structured data, outline, technologies, text, links.
    [Fact]
    public void Sections_keep_priority_order()
    {
        var user = new PromptBuilder().Build(Digests.Simple(), Tech, Options()).User;

        var order = new[] { "Title:", "Description:", "Language:", "Structured data:", "Outline:", "Technologies:", "Text:", "Links:" }
            .Select(label => user.IndexOf(label, StringComparison.Ordinal))
            .ToList();

        Assert.All(order, index => Assert.True(index >= 0));
        Assert.Equal(order.OrderBy(i => i), order);
    }

    // At the smallest allowed budget the highest priorities (title, description) are still there and
    // the limit still holds.
    [Fact]
    public void Smallest_budget_keeps_the_highest_priority_facts()
    {
        var minimum = PromptBuilder.MinimumInputTokens;
        var request = new PromptBuilder().Build(Digests.Huge(), Tech, Options(minimum));

        Assert.Contains("Title:", request.User);
        Assert.Contains("Description:", request.User);
        Assert.True(InputTokens(request) <= minimum);
    }

    // On a big page the text is cut at a word boundary and the top links still get some room.
    [Fact]
    public void Big_page_text_is_truncated_without_losing_the_links()
    {
        var user = new PromptBuilder().Build(Digests.Huge(), Tech, Options(1500)).User;

        var text = user[(user.IndexOf("Text:\n", StringComparison.Ordinal) + 6)..user.IndexOf("\n\nLinks:", StringComparison.Ordinal)];
        var lastWord = text.TrimEnd().Split(' ')[^1];
        Assert.Contains(lastWord, new[] { "lorem", "ipsum", "dolor", "sit", "amet" });
        Assert.True(text.Length < 4000 * 27, "the long text should have been cut");
        Assert.Contains("Links:\n- Link 0 -> ", user);
    }

    // Only a handful of links go in, never the whole list.
    [Fact]
    public void Only_the_top_links_are_sent()
    {
        var user = new PromptBuilder().Build(Digests.Huge(), Tech, Options(8000)).User;

        var linkLines = user.Split('\n').Count(line => line.StartsWith("- Link ", StringComparison.Ordinal));
        Assert.InRange(linkLines, 1, 15);
    }

    // Raw markup never makes it through as a block of its own and control characters are removed.
    [Fact]
    public void Control_characters_are_removed_from_the_prompt()
    {
        var digest = Digests.Simple(text: "Hello\u0000 wor\u0007ld\r\n\r\n\r\nSecond line");

        var user = new PromptBuilder().Build(digest, Tech, Options()).User;

        Assert.DoesNotContain('\u0000', user);
        Assert.DoesNotContain('\u0007', user);
        Assert.DoesNotContain('\r', user);
        Assert.Contains("Hello wor ld\nSecond line", user);
    }

    // The fixed instructions are byte-identical for every page, so provider prompt caching can apply,
    // and no page data, user data or URL leaks into them.
    [Fact]
    public void System_prefix_is_byte_identical_across_pages()
    {
        var builder = new PromptBuilder();
        var a = builder.Build(Digests.Simple("a", "Page A"), Tech, Options());
        var b = builder.Build(Digests.Huge("b"), [], Options(1000));

        Assert.Equal(Encoding.UTF8.GetBytes(a.System), Encoding.UTF8.GetBytes(b.System));
        Assert.Equal(PromptBuilder.SystemPrompt, a.System);
        Assert.DoesNotContain("Page A", a.System);
        Assert.NotEqual(a.User, b.User);
    }

    // The URL is not part of the prompt (the answer depends on the page content only).
    [Fact]
    public void Prompt_contains_no_url_or_timestamp()
    {
        var request = new PromptBuilder().Build(Digests.Simple(), Tech, Options());

        Assert.DoesNotContain("URL:", request.User);
        Assert.DoesNotContain("Scanned", request.User);
        Assert.DoesNotContain("2026", request.System);
    }

    // The model and output cap come from the options; the fast model is the default.
    [Fact]
    public void Request_uses_configured_model_and_output_cap()
    {
        var builder = new PromptBuilder();

        var fast = builder.Build(Digests.Simple(), Tech, Options());
        var quality = builder.Build(Digests.Simple(), Tech, Options(), useQualityModel: true);

        Assert.Equal("fast-model", fast.Model);
        Assert.Equal("quality-model", quality.Model);
        Assert.Equal(700, fast.MaxOutputTokens);
    }

    // A missing model name fails with a clear message instead of sending an empty model.
    [Fact]
    public void Missing_model_name_is_a_clear_error()
    {
        var options = Options();
        options.FastModel = " ";

        var error = Assert.Throws<InvalidOperationException>(() => new PromptBuilder().Build(Digests.Simple(), Tech, options));

        Assert.Contains("Ai:FastModel", error.Message);
    }

    // An input budget too small for the fixed instructions is a clear error.
    [Fact]
    public void Too_small_budget_is_a_clear_error()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new PromptBuilder().Build(Digests.Simple(), Tech, Options(10)));

        Assert.Contains("Ai:MaxInputTokens", error.Message);
    }

    // The token estimate: 1 token per 4 ASCII characters (rounded up), 1 per other character.
    [Fact]
    public void Token_estimate_is_conservative()
    {
        Assert.Equal(0, TokenEstimator.Estimate(""));
        Assert.Equal(1, TokenEstimator.Estimate("abc"));
        Assert.Equal(1, TokenEstimator.Estimate("abcd"));
        Assert.Equal(2, TokenEstimator.Estimate("abcde"));
        Assert.Equal(3, TokenEstimator.Estimate("日本語"));
    }

    // Truncation never cuts a word in half and never exceeds the budget.
    [Fact]
    public void Truncation_cuts_at_word_boundaries_within_budget()
    {
        var text = "alpha beta gamma delta epsilon";

        var cut = TokenEstimator.TruncateToQuarters(text, 13);

        Assert.Equal("alpha beta", cut);
        Assert.True(TokenEstimator.Quarters(cut) <= 13);
        Assert.Equal(text, TokenEstimator.TruncateToQuarters(text, 1000));
        Assert.Equal(string.Empty, TokenEstimator.TruncateToQuarters(text, 0));
    }

    // List truncation keeps whole lines only.
    [Fact]
    public void Truncation_of_lists_keeps_whole_lines()
    {
        var list = "- first item\n- second item\n- third item";

        var cut = TokenEstimator.TruncateToQuarters(list, 20, wholeLines: true);

        Assert.Equal("- first item", cut);
    }
}
