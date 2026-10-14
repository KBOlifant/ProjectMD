using System.Text;

namespace ProjectMD.Api.Services.Extraction.Design;

// Holds the text of the stylesheets a page loaded, only for the short time between the page loading
// and the design summary being built. Each sheet is cut to 256 KB, at most 40 sheets and about
// 2 MB in total are kept, and the texts are dropped when the fetch ends. Safe to call from the
// browser's request handlers (which run on several threads).
public sealed class StyleSheetTexts
{
    // The most text kept in total across all sheets.
    private const int MaxTotalChars = 2 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly List<string> _sheets = [];
    private int _totalChars;

    // Remembers the body of a response if it is a stylesheet; other kinds of request are ignored.
    public void Add(string resourceType, byte[] body)
    {
        if (!string.Equals(resourceType, "stylesheet", StringComparison.OrdinalIgnoreCase) || body.Length == 0)
        {
            return;
        }

        var length = Math.Min(body.Length, CssAnalyzer.MaxSheetChars);
        var text = Encoding.UTF8.GetString(body, 0, length);
        lock (_gate)
        {
            if (_sheets.Count >= CssAnalyzer.MaxSheets || _totalChars + text.Length > MaxTotalChars)
            {
                return;
            }

            _sheets.Add(text);
            _totalChars += text.Length;
        }
    }

    // A copy of the sheets collected so far.
    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            return [.. _sheets];
        }
    }
}
