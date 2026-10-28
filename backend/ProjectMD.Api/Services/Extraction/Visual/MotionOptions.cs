namespace ProjectMD.Api.Services.Extraction.Visual;

// Settings for the visual part of a scan: screenshots taken while scrolling, the animation evidence
// read from the page, and (used by the AI stage) how those screenshots are sent to a vision model.
// Bound from the "Motion" section of configuration (backend/ProjectMD.Api/appsettings.json).
//
// Every default here keeps the old behaviour: Enabled is false, so without the "Motion" section a
// scan takes no screenshots, loads no images and uses the old 1280x800 window. appsettings.json
// switches it on. See docs/SCANNER-SETTINGS.md for what each value does.
public sealed class MotionOptions
{
    // The configuration section these settings are read from.
    public const string SectionName = "Motion";

    // ---- Capture (the scraper) -------------------------------------------------------------------

    // Master switch for screenshots, image loading and animation evidence (Motion:Enabled).
    public bool Enabled { get; set; }

    // The browser window size used while scanning (Motion:ViewportWidth / Motion:ViewportHeight).
    // Many award-style sites switch their effects off on small screens, so a desktop size is used.
    public int ViewportWidth { get; set; } = 1440;

    public int ViewportHeight { get; set; } = 900;

    // When true, images and fonts are loaded (still through the same address checks as everything
    // else) so screenshots look like the real page (Motion:LoadImagesAndFonts).
    public bool LoadImagesAndFonts { get; set; } = true;

    // The most bytes of images and fonts one page may load in total; later ones are skipped
    // (Motion:MaxImageAndFontBytes). Counted separately from Scanner:MaxTotalBytes so images can
    // never starve the scripts a page needs.
    public int MaxImageAndFontBytes { get; set; } = 15 * 1024 * 1024;

    // The most image and font requests one page may make (Motion:MaxImageAndFontRequests).
    public int MaxImageAndFontRequests { get; set; } = 150;

    // How many screenshots to take while scrolling down, the first one included (Motion:ScrollFrames).
    public int ScrollFrames { get; set; } = 6;

    // How far to scroll between screenshots, in pixels (Motion:ScrollStepPixels).
    public int ScrollStepPixels { get; set; } = 760;

    // How long to wait after each scroll so scroll-triggered animations can play (Motion:FrameDelayMs).
    public int FrameDelayMs { get; set; } = 700;

    // JPEG quality of the screenshots, 1 to 100 (Motion:ScreenshotQuality). Lower is smaller.
    public int ScreenshotQuality { get; set; } = 60;

    // The scroll screenshots are combined into grid images ("contact sheets") so several frames fit
    // into one image slot of the vision model. Columns and rows per sheet (Motion:SheetColumns /
    // Motion:SheetRows).
    public int SheetColumns { get; set; } = 3;

    public int SheetRows { get; set; } = 2;

    // Hard time limit for all the screenshots and evidence of one page, in seconds
    // (Motion:CaptureTimeoutSeconds). When it runs out, whatever was captured so far is kept.
    public int CaptureTimeoutSeconds { get; set; } = 20;

    // How many elements to hover with the mouse to find hover states, 0 to switch it off
    // (Motion:HoverProbes). Each costs roughly half a second of the capture time.
    public int HoverProbes { get; set; } = 10;

    // How long to wait after the mouse arrives before reading the hover style, so the element's own
    // transition can finish (Motion:HoverSettleMs).
    public int HoverSettleMs { get; set; } = 260;

    // ---- AI (the vision model) -------------------------------------------------------------------

    // When true (and Scanner:Mode is ai with a Groq key), the screenshots and evidence are sent to
    // the vision model to list the animations (Motion:AiEnabled).
    public bool AiEnabled { get; set; } = true;

    // The Groq model that can read images (Motion:VisionModel). It must support image input.
    public string VisionModel { get; set; } = "qwen/qwen3.8-27b";

    // The most images sent in one request (Motion:MaxImagesPerRequest). Groq's qwen/qwen3.8-27b
    // accepts at most 3.
    public int MaxImagesPerRequest { get; set; } = 3;

    // How many input tokens Groq counts per image, used for the budget estimate
    // (Motion:TokensPerImage). 2048 for qwen/qwen3.8-27b.
    public int TokensPerImage { get; set; } = 2048;

    // Hard cap on the answer length in tokens (Motion:MaxOutputTokens). The answer holds the
    // animation descriptions and the design breakdown, so it needs more room than the text notes.
    public int MaxOutputTokens { get; set; } = 3000;

    // How the answer's JSON is requested (Motion:ResponseFormat): "json_object" (default; any valid
    // JSON, the prompt describes the shape; Groq's vision docs show it working with images) or
    // "json_schema" (strict schema decoding; stricter, but a provider may refuse it with images).
    public string ResponseFormat { get; set; } = "json_object";

    // Seconds to wait for the vision model (Motion:TimeoutSeconds). Reading images takes longer
    // than the text call, so this replaces Ai:TimeoutSeconds for the vision call only.
    public int TimeoutSeconds { get; set; } = 60;

    // How much the vision model thinks before answering (Motion:ReasoningEffort): "none" (fastest
    // and cheapest), "low", "medium", "high" or "default".
    public string ReasoningEffort { get; set; } = "none";

    // True when every number is in a sensible range. Used by the options validation.
    public static bool IsValid(MotionOptions o) =>
        o.ViewportWidth is >= 320 and <= 3840 &&
        o.ViewportHeight is >= 320 and <= 2400 &&
        o.MaxImageAndFontBytes is >= 0 and <= 200_000_000 &&
        o.MaxImageAndFontRequests is >= 0 and <= 2000 &&
        o.ScrollFrames is >= 1 and <= 24 &&
        o.ScrollStepPixels is >= 50 and <= 5000 &&
        o.FrameDelayMs is >= 0 and <= 10_000 &&
        o.ScreenshotQuality is >= 1 and <= 100 &&
        o.SheetColumns is >= 1 and <= 6 &&
        o.SheetRows is >= 1 and <= 6 &&
        o.CaptureTimeoutSeconds is >= 2 and <= 120 &&
        o.HoverProbes is >= 0 and <= 30 &&
        o.HoverSettleMs is >= 0 and <= 2000 &&
        o.MaxImagesPerRequest is >= 1 and <= 10 &&
        o.TokensPerImage is >= 0 and <= 20_000 &&
        o.MaxOutputTokens is >= 100 and <= 16_000 &&
        o.ResponseFormat is "json_object" or "json_schema" &&
        o.TimeoutSeconds is >= 5 and <= 300 &&
        o.VisionModel.Length <= 100 &&
        o.ReasoningEffort is "none" or "low" or "medium" or "high" or "default";

    // How many scroll frames fit on one contact sheet.
    public int FramesPerSheet => Math.Max(1, SheetColumns * SheetRows);
}
