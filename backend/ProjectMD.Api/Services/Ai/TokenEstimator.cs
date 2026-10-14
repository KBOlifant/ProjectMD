namespace ProjectMD.Api.Services.Ai;

// A cheap, offline guess of how many tokens a piece of text costs, used to keep prompts under
// Ai:MaxInputTokens and to reserve budget before a call. It is an approximation, not the
// provider's tokenizer: plain ASCII text counts as 1 token per 4 characters (the same rule
// PageDigest.EstimatedTokens uses) and every non-ASCII UTF-16 character counts as 1 whole token,
// which over-counts on purpose so non-Latin text cannot sneak past the limit. Real token counts
// come back from the provider and are what the usage ledger stores.
public static class TokenEstimator
{
    // Work in quarter tokens so a running total over many pieces is exact: ASCII = 1, other = 4.
    private const int QuartersPerToken = 4;

    // Estimated tokens for the text (rounded up; 0 for empty text).
    public static int Estimate(string? text) => (int)Math.Min(int.MaxValue, ToTokens(Quarters(text)));

    // Cost of the text in quarter tokens. Adding two results is the same as measuring the joined text.
    public static long Quarters(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        long quarters = 0;
        foreach (var c in text)
        {
            quarters += c < 128 ? 1 : QuartersPerToken;
        }

        return quarters;
    }

    // Turns quarter tokens into whole tokens, rounding up.
    public static long ToTokens(long quarters) => (quarters + QuartersPerToken - 1) / QuartersPerToken;

    // Converts a token budget into quarter tokens.
    public static long ToQuarters(long tokens) => tokens * QuartersPerToken;

    // Cuts the text to the longest prefix that fits in `maxQuarters`, never in the middle of a word
    // or a surrogate pair, trimming trailing whitespace. Returns the text unchanged if it fits.
    // When `wholeLines` is true it cuts at the last line break instead (for lists). The result
    // never ends with a partial item.
    public static string TruncateToQuarters(string text, long maxQuarters, bool wholeLines = false)
    {
        if (maxQuarters <= 0 || text.Length == 0)
        {
            return string.Empty;
        }

        long used = 0;
        var end = 0;
        while (end < text.Length)
        {
            var cost = text[end] < 128 ? 1 : QuartersPerToken;
            if (used + cost > maxQuarters)
            {
                break;
            }

            used += cost;
            end++;
        }

        if (end >= text.Length)
        {
            return text;
        }

        // Do not split a surrogate pair: drop a dangling high surrogate.
        if (end > 0 && char.IsHighSurrogate(text[end - 1]))
        {
            end--;
        }

        // Back off to a boundary: a line break for lists, otherwise any whitespace. If the cut
        // already lands exactly on a boundary (the next character is a break) keep it.
        var atBoundary = wholeLines ? text[end] == '\n' : char.IsWhiteSpace(text[end]);
        if (!atBoundary)
        {
            var boundary = LastBoundaryBefore(text, end, wholeLines);
            end = boundary > 0 ? boundary : (wholeLines ? 0 : end);
        }

        return text[..end].TrimEnd();
    }

    // Index of the last line break (wholeLines) or whitespace character strictly before `end`, or -1.
    private static int LastBoundaryBefore(string text, int end, bool wholeLines)
    {
        for (var i = end - 1; i >= 0; i--)
        {
            if (wholeLines ? text[i] == '\n' : char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
