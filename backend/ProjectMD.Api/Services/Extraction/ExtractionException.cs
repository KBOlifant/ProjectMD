namespace ProjectMD.Api.Services.Extraction;

// Why a page could not be extracted. The controller can use it to pick a status code.
public enum ExtractionFailure
{
    // The site could not be reached, refused, redirected in a loop or has a bad certificate.
    Unreachable,

    // The site (or the whole fetch) took too long.
    TimedOut,

    // The address (or something it redirected to) is not a public web address.
    BlockedAddress,

    // The address did not return an HTML page (a file, an image, JSON and so on).
    NotHtml,

    // The page is too large to scan.
    TooLarge,

    // The browser that renders pages is not installed or could not start on the server.
    RendererMissing,

    // The site answered with an HTTP error status (404, 500 and so on).
    HttpError,

    // All render slots stayed busy for too long.
    Busy,
}

// The one exception the extraction code throws for expected failures. Its Message is always safe to
// show to a user: no stack trace, no internal address, no URL. The original exception, if any, is
// kept as InnerException for server logs only.
public sealed class ExtractionException : Exception
{
    // A failure of an unspecified kind (treated as Unreachable) with a user-safe message.
    public ExtractionException(string message)
        : this(ExtractionFailure.Unreachable, message)
    {
    }

    // A failure of a known kind with a user-safe message and an optional inner exception for logs.
    public ExtractionException(ExtractionFailure failure, string message, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
    }

    // What went wrong, in broad terms.
    public ExtractionFailure Failure { get; }

    // True when the problem is the address the user gave (so a 400 fits); false when the site or
    // our renderer is at fault (so a 502 fits).
    public bool IsClientError => Failure is ExtractionFailure.BlockedAddress or ExtractionFailure.NotHtml;
}
