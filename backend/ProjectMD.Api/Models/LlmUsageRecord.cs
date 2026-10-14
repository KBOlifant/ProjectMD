namespace ProjectMD.Api.Models;

// One line in the AI usage ledger: a call to the language model (or a cache hit that avoided one).
// The daily token caps are enforced by summing these rows for the current UTC day. It stores token
// counts and the model name only: never page content, prompts or answers.
public class LlmUsageRecord
{
    // Primary key.
    public Guid Id { get; set; }

    // Whose scan caused the call (foreign key to AppUser). Deleting the user deletes their rows.
    public Guid UserId { get; set; }

    // The saved scan this belongs to, when known. Usually null: the usage is recorded while the
    // scan is being generated, before the scan row exists. Not a foreign key on purpose.
    public Guid? ScanId { get; set; }

    // The model that answered (max 100 characters).
    public string Model { get; set; } = string.Empty;

    // Input tokens as reported by the provider (0 for a cache hit).
    public int PromptTokens { get; set; }

    // Output tokens as reported by the provider (0 for a cache hit).
    public int CompletionTokens { get; set; }

    // True when the answer came from the cache and no credits were spent.
    public bool CacheHit { get; set; }

    // When it was recorded (UTC).
    public DateTime CreatedAt { get; set; }
}
