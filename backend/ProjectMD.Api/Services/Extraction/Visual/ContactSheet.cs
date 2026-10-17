using System.Globalization;
using System.Text;

namespace ProjectMD.Api.Services.Extraction.Visual;

// Lays scroll screenshots out in a grid with a numbered caption on each, so several frames fit
// into ONE image for the vision model (Groq accepts only a few images per request). Pure: it only
// builds the HTML; VisualCapturer renders it in a blank, script-free browser page and takes one
// screenshot of it. The images are our own JPEG bytes as data: URLs, so nothing is loaded.
public static class ContactSheet
{
    // The width of one frame on the sheet: half the window width, but never tiny.
    public static int ThumbWidth(MotionOptions options) => Math.Max(160, options.ViewportWidth / 2);

    // The height that keeps the window's shape at ThumbWidth.
    public static int ThumbHeight(MotionOptions options) =>
        Math.Max(100, (int)Math.Round(ThumbWidth(options) * (double)options.ViewportHeight / options.ViewportWidth));

    // The frame indexes (0-based) that go on each sheet, in order. Frame 0 (the first screen) is
    // included, so the sheets show the whole scroll on their own.
    public static IReadOnlyList<IReadOnlyList<int>> Pages(int frameCount, MotionOptions options)
    {
        var pages = new List<IReadOnlyList<int>>();
        var perSheet = options.FramesPerSheet;
        for (var start = 0; start < frameCount; start += perSheet)
        {
            pages.Add(Enumerable.Range(start, Math.Min(perSheet, frameCount - start)).ToList());
        }

        return pages;
    }

    // The caption for one frame, for example "Frame 3 - scrolled 1520 px - 1.4 s".
    public static string Caption(int index, MotionOptions options)
    {
        var px = index * options.ScrollStepPixels;
        var seconds = index * options.FrameDelayMs / 1000.0;
        return "Frame " + (index + 1).ToString(CultureInfo.InvariantCulture)
            + " - scrolled " + px.ToString(CultureInfo.InvariantCulture) + " px - "
            + seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
    }

    // The whole HTML page for one sheet.
    public static string BuildHtml(IReadOnlyList<byte[]> frames, IReadOnlyList<int> indexes, MotionOptions options)
    {
        var w = ThumbWidth(options).ToString(CultureInfo.InvariantCulture);
        var h = ThumbHeight(options).ToString(CultureInfo.InvariantCulture);
        var cols = Math.Max(1, options.SheetColumns).ToString(CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><style>");
        sb.Append("html,body{margin:0;padding:0;background:#000;}");
        sb.Append(".grid{display:grid;grid-template-columns:repeat(").Append(cols).Append(',').Append(w).Append("px);}");
        sb.Append("figure{margin:0;position:relative;width:").Append(w).Append("px;height:").Append(h).Append("px;overflow:hidden;outline:2px solid #000;}");
        sb.Append("img{display:block;width:").Append(w).Append("px;height:").Append(h).Append("px;object-fit:cover;}");
        sb.Append("figcaption{position:absolute;left:0;top:0;background:#000;color:#fff;font:bold 18px/1.3 Arial,Helvetica,sans-serif;padding:4px 10px;}");
        sb.Append("</style></head><body><div class=\"grid\">");
        foreach (var i in indexes)
        {
            if (i < 0 || i >= frames.Count)
            {
                continue;
            }

            sb.Append("<figure><img alt=\"\" src=\"data:image/jpeg;base64,")
                .Append(Convert.ToBase64String(frames[i]))
                .Append("\"><figcaption>")
                .Append(Caption(i, options))
                .Append("</figcaption></figure>");
        }

        sb.Append("</div></body></html>");
        return sb.ToString();
    }
}
