namespace ProjectMD.Api.Services.Extraction.Components;

// The computed styles of one real element on the page (a button, an input...), as plain CSS
// property names and values, plus its short visible text (lines joined with " | ") and the hex
// colour painted behind it on the page.
public sealed record ComponentStyle(
    string Kind,
    string Tag,
    IReadOnlyDictionary<string, string> Styles,
    string Text = "",
    string Backdrop = "");

// Everything the in-page script found: at most a few elements of each kind.
public sealed record ComponentStyles(IReadOnlyList<ComponentStyle> Items);

// One small HTML/CSS version of a component, written by the AI and cleaned by ComponentSanitizer.
// It is shown inside a sandboxed frame with scripts off, on Background (the hex colour behind the
// real component, "" when unknown), and offered for copying.
public sealed record ComponentPreview(string Kind, string Name, string Html, string Css, string Background = "");

// The cleaned previews stored with a scan (ScanAnalysis.Components).
public sealed record ComponentPreviews(IReadOnlyList<ComponentPreview> Items);
