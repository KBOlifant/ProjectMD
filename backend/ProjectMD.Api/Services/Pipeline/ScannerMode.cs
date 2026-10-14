namespace ProjectMD.Api.Services.Pipeline;

// How scans are produced (configuration key Scanner:Mode, default "static").
public enum ScannerMode
{
    // The MOCK generator: nothing is fetched, nothing changes from before.
    Static,

    // Real fetch + reduce + fingerprint; the document is built without any AI.
    Extract,

    // Everything in Extract, plus the AI writes the short prose parts (when an AI client is
    // configured; otherwise it behaves like Extract).
    Ai,
}

// The "Scanner" section of configuration, as far as the pipeline cares: only the mode. (The
// extraction code reads its own keys from the same section.)
public sealed class ScannerModeOptions
{
    // Scanner:Mode as text: "static", "extract" or "ai" (any letter case). Empty means static.
    public string Mode { get; set; } = "static";

    // The mode as an enum. Throws InvalidOperationException with a clear message for any other
    // value, so a typo stops the app at startup instead of silently running the mock.
    public ScannerMode Resolve() => Parse(Mode);

    // Turns the configured text into a ScannerMode (null or blank = Static).
    public static ScannerMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ScannerMode.Static;
        }

        return Enum.TryParse<ScannerMode>(value.Trim(), ignoreCase: true, out var mode) && Enum.IsDefined(mode)
            ? mode
            : throw new InvalidOperationException(
                $"Scanner:Mode must be 'static', 'extract' or 'ai' (it was '{value.Trim()}').");
    }
}
