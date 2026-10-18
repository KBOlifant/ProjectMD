namespace ProjectMD.Api.Services.Extraction.Components;

// One element of a captured component: its tag, the computed CSS (property name to value, already
// limited to a fixed list), the type of an input, whether it holds text of its own (the text itself
// is never captured), and its child elements.
public sealed record ComponentNode(
    string Tag,
    IReadOnlyDictionary<string, string> Styles,
    string? InputType,
    bool HasText,
    IReadOnlyList<ComponentNode> Children);

// One real component from the page (a button, an input, a card...): its kind, the colour of the page
// behind it, its height in pixels and the element tree with computed styles. Styles only, no text.
public sealed record ComponentSnapshot(string Kind, string Background, int Height, ComponentNode Root);

// Everything the in-page script found: a few snapshots of each kind.
public sealed record ComponentStyles(IReadOnlyList<ComponentSnapshot> Items);

// One component ready to show: clean HTML and CSS built from a snapshot (see ComponentPreviewBuilder
// and ComponentSanitizer), the page colour behind it and a suggested frame height. It is shown inside
// a sandboxed frame with scripts off, and offered for copying. Scans made before Background and
// Height existed have them empty.
public sealed record ComponentPreview(string Kind, string Name, string Html, string Css, string Background = "", int Height = 0);

// The previews stored with a scan (ScanAnalysis.Components).
public sealed record ComponentPreviews(IReadOnlyList<ComponentPreview> Items);
