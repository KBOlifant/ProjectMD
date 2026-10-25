namespace ProjectMD.Api.Services.Extraction;

// Data types for the visual capture of a page: screenshots taken while scrolling, and the animation
// evidence read from the running page. Screenshots are JPEG bytes kept in memory for the length of
// one scan only; they are never stored in the database or written into the Markdown.

// One animation the browser itself reported, so its timing is MEASURED, not guessed.
// Source is "css-animation", "css-transition", "web-animation" or "gsap". Target is a short
// description of the element such as "h1#title.hero-heading". Name is the @keyframes name (CSS
// animations) or the property (CSS transitions). Properties are the CSS properties that change.
// Values are the first and last keyframe values of the properties that matter for rebuilding it
// (opacity, transform, translate, scale, rotate; for GSAP: opacity, x, y, xPercent, yPercent, scale,
// rotation), or null when none were read.
public sealed record MeasuredAnimation(
    string Source,
    string Target,
    string? Name,
    int? DurationMs,
    int? DelayMs,
    string? Easing,
    string? Repeat,
    IReadOnlyList<string> Properties,
    IReadOnlyList<MotionValue>? Values = null);

// One property's start and end value as the page reported them, each null when not known (a GSAP
// "to" tween has no start value in its settings). Raw page text: treat as untrusted.
public sealed record MotionValue(string Property, string? From, string? To);

// One GSAP ScrollTrigger: the element that triggers it, where it starts and ends (GSAP's own words,
// for example "top 80%"), whether the animation follows the scrollbar (scrub) and whether the
// element is pinned in place while scrolling.
public sealed record ScrollTriggerInfo(string Trigger, string? Start, string? End, string? Scrub, bool Pinned);

// Everything the scanner could measure about motion on the page. Libraries are display names such
// as "GSAP", "ScrollTrigger", "Lenis" or "Three.js". Signals are short facts such as
// "3 <canvas> elements" or "12 @keyframes rules in the CSS".
public sealed record MotionEvidence(
    IReadOnlyList<string> Libraries,
    IReadOnlyList<MeasuredAnimation> Animations,
    IReadOnlyList<ScrollTriggerInfo> ScrollTriggers,
    IReadOnlyList<string> Signals)
{
    // True when nothing at all was found.
    public bool IsEmpty => Libraries.Count == 0 && Animations.Count == 0 && ScrollTriggers.Count == 0 && Signals.Count == 0;
}

// The screenshots of one page. Frames[0] is the first screen; each later frame was taken after
// scrolling ScrollStepPixels further down and waiting FrameDelayMs. Sheets are the same frames laid
// out in grids (contact sheets) so several fit into one image for the vision model. All images are
// JPEG. Motion is null when the evidence could not be read.
public sealed record VisualCapture(
    int ViewportWidth,
    int ViewportHeight,
    int ScrollStepPixels,
    int FrameDelayMs,
    IReadOnlyList<byte[]> Frames,
    IReadOnlyList<byte[]> Sheets,
    MotionEvidence? Motion);
