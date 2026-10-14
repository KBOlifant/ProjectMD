using System.Text.RegularExpressions;

namespace ProjectMD.Api.Services.Extraction.Design;

// Decides whether a page is styled with a utility-class framework (Tailwind style: "flex px-4
// text-sm md:grid"). It looks only at class-name lists, which the in-page script sends for
// elements with at least three classes.
public static partial class UtilityFrameworkDetector
{
    // More than this share of the sampled elements must carry 3+ utility classes.
    public const double Threshold = 0.40;

    // How many of an element's classes must look like utilities for it to count.
    private const int MinUtilityClasses = 3;

    // Whole-word utilities that are strongly Tailwind-like on their own.
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "flex", "grid", "block", "inline", "inline-block", "inline-flex", "hidden", "relative", "absolute", "fixed",
        "sticky", "container", "truncate", "uppercase", "lowercase", "capitalize", "underline", "italic", "antialiased",
        "transition", "rounded", "border", "shadow", "mx-auto", "sr-only", "flex-col", "flex-row", "flex-wrap",
        "flex-1", "items-center", "justify-center", "justify-between", "overflow-hidden", "w-full", "h-full",
    };

    // Returns "Tailwind" when more than 40% of the sampled elements have 3+ utility classes and the
    // page shows Tailwind-specific signs (a variant prefix such as "md:" or a Tailwind size word
    // such as "text-sm"), otherwise null. sampled is the number of elements the script looked at.
    public static string? Detect(int sampled, IEnumerable<ClassSample> classSets)
    {
        if (sampled <= 0)
        {
            return null;
        }

        var utilityElements = 0;
        var tailwindSigns = false;
        foreach (var set in classSets)
        {
            var utilities = 0;
            foreach (var name in set.Classes)
            {
                if (!IsUtilityClass(name))
                {
                    continue;
                }

                utilities++;
                tailwindSigns |= VariantRegex().IsMatch(name) || TailwindScaleRegex().IsMatch(name);
            }

            if (utilities >= MinUtilityClasses)
            {
                utilityElements += set.Count;
            }
        }

        return tailwindSigns && utilityElements / (double)sampled > Threshold ? "Tailwind" : null;
    }

    // True when one class name looks like a utility class (keyword, or prefix-value such as "px-4").
    public static bool IsUtilityClass(string name) =>
        name.Length is > 0 and <= 80 && (Keywords.Contains(name) || UtilityRegex().IsMatch(name));

    // Variant prefixes: md:, hover:, dark:, focus:...
    [GeneratedRegex(@"^(?:sm|md|lg|xl|2xl|hover|focus|active|dark|group-hover|disabled|first|last|peer-[a-z]+):", RegexOptions.CultureInvariant)]
    private static partial Regex VariantRegex();

    // Scale words that only Tailwind-style frameworks use together with a property prefix.
    [GeneratedRegex(@"^(?:(?:sm|md|lg|xl|2xl|hover|focus|dark):)*(?:text-(?:xs|sm|base|lg|xl|[2-9]xl)|rounded-(?:sm|md|lg|xl|2xl|3xl|full)|shadow-(?:sm|md|lg|xl|2xl)|p[xytblr]?-(?:0\.5|1\.5|2\.5|3\.5|\d+)|gap-\d+|space-[xy]-\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex TailwindScaleRegex();

    // property-value utilities, with optional variant prefixes and arbitrary values in brackets.
    [GeneratedRegex(@"^(?:[a-z0-9-]+:)*-?(?:p|px|py|pt|pb|pl|pr|m|mx|my|mt|mb|ml|mr|gap|gap-x|gap-y|w|h|min-w|min-h|max-w|max-h|size|text|bg|border|ring|rounded|font|shadow|space-x|space-y|leading|tracking|z|top|left|right|bottom|inset|opacity|items|justify|content|self|col|row|grid-cols|flex|order|overflow|object|cursor|transition|duration|ease|fill|stroke|divide|outline)-[A-Za-z0-9./%\[\]#_-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex UtilityRegex();
}
