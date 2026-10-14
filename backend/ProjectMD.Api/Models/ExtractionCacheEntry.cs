namespace ProjectMD.Api.Models;

// One cached piece of AI-written prose for a page, so the same page content is never paid for
// twice. The composite key (ContentHash, PromptVersion, Model) is the whole lookup: the same page
// content, written with the same prompt by the same model, gives the same answer, and it is reused
// for any user. Only public page-derived text is stored here: no page HTML, no user data.
public class ExtractionCacheEntry
{
    // SHA-256 hex of the normalised page content (PageDigest.ContentHash). 64 characters.
    public string ContentHash { get; set; } = string.Empty;

    // The prompt template version the text was written with (PromptBuilder.PromptVersion). Bumping
    // the version makes every older entry unreachable, so a changed prompt never serves old text.
    public int PromptVersion { get; set; }

    // The model name the text was written by (max 100 characters).
    public string Model { get; set; } = string.Empty;

    // The cached AI prose (Markdown). This is the writing only, not the whole scan document: the
    // document is rebuilt for every scan around it (URL, date, tech list, outline).
    public string Markdown { get; set; } = string.Empty;

    // When the entry was written or last refreshed (UTC).
    public DateTime CreatedAt { get; set; }

    // When the entry stops being served (UTC). Expired rows are ignored and overwritten on the next
    // store.
    public DateTime ExpiresAt { get; set; }

    // How many times the entry has been served instead of calling the AI.
    public int HitCount { get; set; }
}
