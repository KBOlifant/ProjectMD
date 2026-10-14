using System.Net;

namespace ProjectMD.Api.Services.Extraction;

// Answers two questions about a host name: is it on the same site as the scanned page (first party),
// and, if not, what kind of service is it (analytics, fonts, payments...). "Same site" uses a simple
// rule: the same registrable domain, taken as the last two labels, or the last three when the
// domain ends in a known two-part suffix such as co.uk. It is not a full public-suffix list, which
// is good enough for grouping. Pure and deterministic.
public static class HostClassifier
{
    // Two-part public suffixes that need three labels to form a registrable domain.
    private static readonly HashSet<string> MultiPartSuffixes = new(StringComparer.Ordinal)
    {
        "co.uk", "org.uk", "ac.uk", "gov.uk", "me.uk", "ltd.uk", "plc.uk",
        "com.au", "net.au", "org.au", "edu.au", "gov.au",
        "co.za", "org.za", "gov.za", "ac.za", "net.za",
        "co.nz", "org.nz", "net.nz",
        "co.jp", "ne.jp", "or.jp", "ac.jp",
        "com.br", "net.br", "org.br", "com.mx", "com.ar", "com.co",
        "co.in", "net.in", "org.in", "co.kr", "or.kr",
        "com.cn", "net.cn", "org.cn", "com.hk", "com.sg", "com.my", "com.tr", "com.tw", "com.ua",
        "co.il", "co.id", "co.th", "co.ke", "co.ug", "com.ng", "com.eg", "com.pk", "com.ph", "com.vn",
    };

    // Known third-party services by registrable domain or host. A lookup tries the full host first,
    // then each shorter suffix of it.
    private static readonly Dictionary<string, string> Categories = new(StringComparer.Ordinal)
    {
        // analytics
        ["google-analytics.com"] = "analytics",
        ["googletagmanager.com"] = "analytics",
        ["analytics.google.com"] = "analytics",
        ["segment.io"] = "analytics",
        ["segment.com"] = "analytics",
        ["mixpanel.com"] = "analytics",
        ["amplitude.com"] = "analytics",
        ["hotjar.com"] = "analytics",
        ["hotjar.io"] = "analytics",
        ["plausible.io"] = "analytics",
        ["posthog.com"] = "analytics",
        ["heap.io"] = "analytics",
        ["heapanalytics.com"] = "analytics",
        ["fullstory.com"] = "analytics",
        ["clarity.ms"] = "analytics",
        ["matomo.cloud"] = "analytics",
        ["hs-analytics.net"] = "analytics",
        ["hsforms.com"] = "analytics",

        // ads
        ["doubleclick.net"] = "ads",
        ["googlesyndication.com"] = "ads",
        ["googleadservices.com"] = "ads",
        ["adnxs.com"] = "ads",
        ["criteo.com"] = "ads",
        ["criteo.net"] = "ads",
        ["ads-twitter.com"] = "ads",
        ["facebook.net"] = "ads",
        ["connect.facebook.net"] = "ads",
        ["taboola.com"] = "ads",
        ["outbrain.com"] = "ads",
        ["adsrvr.org"] = "ads",
        ["pubmatic.com"] = "ads",
        ["rubiconproject.com"] = "ads",

        // fonts
        ["fonts.googleapis.com"] = "fonts",
        ["fonts.gstatic.com"] = "fonts",
        ["typekit.net"] = "fonts",
        ["fontawesome.com"] = "fonts",
        ["fonts.net"] = "fonts",

        // cdn
        ["cloudfront.net"] = "cdn",
        ["jsdelivr.net"] = "cdn",
        ["unpkg.com"] = "cdn",
        ["cdnjs.cloudflare.com"] = "cdn",
        ["fastly.net"] = "cdn",
        ["akamaihd.net"] = "cdn",
        ["akamaized.net"] = "cdn",
        ["bootstrapcdn.com"] = "cdn",
        ["code.jquery.com"] = "cdn",
        ["cloudflare.com"] = "cdn",
        ["azureedge.net"] = "cdn",
        ["b-cdn.net"] = "cdn",
        ["imgix.net"] = "cdn",

        // payments
        ["stripe.com"] = "payments",
        ["stripe.network"] = "payments",
        ["paypal.com"] = "payments",
        ["paypalobjects.com"] = "payments",
        ["braintreegateway.com"] = "payments",
        ["adyen.com"] = "payments",
        ["klarna.com"] = "payments",
        ["checkout.com"] = "payments",
        ["paystack.co"] = "payments",
        ["squareup.com"] = "payments",

        // auth
        ["auth0.com"] = "auth",
        ["okta.com"] = "auth",
        ["clerk.com"] = "auth",
        ["clerk.accounts.dev"] = "auth",
        ["accounts.google.com"] = "auth",
        ["login.microsoftonline.com"] = "auth",
        ["identitytoolkit.googleapis.com"] = "auth",
        ["securetoken.googleapis.com"] = "auth",
        ["amazoncognito.com"] = "auth",

        // chat
        ["intercom.io"] = "chat",
        ["intercomcdn.com"] = "chat",
        ["crisp.chat"] = "chat",
        ["drift.com"] = "chat",
        ["driftt.com"] = "chat",
        ["zendesk.com"] = "chat",
        ["zdassets.com"] = "chat",
        ["tawk.to"] = "chat",
        ["livechatinc.com"] = "chat",
        ["tidio.co"] = "chat",

        // monitoring
        ["sentry.io"] = "monitoring",
        ["ingest.sentry.io"] = "monitoring",
        ["datadoghq.com"] = "monitoring",
        ["newrelic.com"] = "monitoring",
        ["nr-data.net"] = "monitoring",
        ["bugsnag.com"] = "monitoring",
        ["logrocket.com"] = "monitoring",
        ["rollbar.com"] = "monitoring",
        ["raygun.io"] = "monitoring",

        // maps
        ["maps.googleapis.com"] = "maps",
        ["maps.gstatic.com"] = "maps",
        ["mapbox.com"] = "maps",
        ["openstreetmap.org"] = "maps",
        ["tile.openstreetmap.org"] = "maps",
        ["arcgis.com"] = "maps",

        // video
        ["youtube.com"] = "video",
        ["youtube-nocookie.com"] = "video",
        ["ytimg.com"] = "video",
        ["vimeo.com"] = "video",
        ["vimeocdn.com"] = "video",
        ["wistia.com"] = "video",
        ["wistia.net"] = "video",
        ["jwplayer.com"] = "video",
        ["jwpcdn.com"] = "video",
    };

    // The registrable domain of a host: "api.shop.example.co.uk" gives "example.co.uk". IP addresses
    // and single-label names (such as "localhost") are returned unchanged. Lower case.
    public static string RegistrableDomain(string host)
    {
        var lower = (host ?? string.Empty).Trim().Trim('.').ToLowerInvariant();
        if (lower.Length == 0 || IPAddress.TryParse(lower.Trim('[', ']'), out _))
        {
            return lower;
        }

        var labels = lower.Split('.');
        if (labels.Length <= 2)
        {
            return lower;
        }

        var lastTwo = labels[^2] + "." + labels[^1];
        var take = MultiPartSuffixes.Contains(lastTwo) ? 3 : 2;
        return string.Join('.', labels[^Math.Min(take, labels.Length)..]);
    }

    // True when both hosts belong to the same registrable domain (www.example.com and
    // api.example.com are the same site; example.com and example.org are not).
    public static bool IsFirstParty(string host, string pageHost) =>
        RegistrableDomain(host).Length > 0 &&
        string.Equals(RegistrableDomain(host), RegistrableDomain(pageHost), StringComparison.Ordinal);

    // A plain category for a well-known service, or null when the host is not in the table. Tries
    // "a.b.example.com", then "b.example.com", then "example.com".
    public static string? Category(string host)
    {
        var current = (host ?? string.Empty).Trim().Trim('.').ToLowerInvariant();
        while (current.Length > 0)
        {
            if (Categories.TryGetValue(current, out var category))
            {
                return category;
            }

            var dot = current.IndexOf('.');
            if (dot < 0)
            {
                break;
            }

            current = current[(dot + 1)..];
        }

        return null;
    }
}
