using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace ProjectMD.Api.Services;

// Keeps one screenshot per scan somewhere the website can show it, and hands back its address.
public interface IScreenshotStore
{
    // Uploads one JPEG and returns its public https address, or null when uploading is not set up
    // or failed. Never throws for upload trouble: a scan must not fail because of its picture.
    Task<string?> UploadAsync(byte[] jpeg, CancellationToken ct);
}

// The Cloudinary-backed IScreenshotStore. The account comes from configuration only (backend/.env):
// either one Cloudinary__Url / CLOUDINARY_URL ("cloudinary://key:secret@cloud") or the three parts
// Cloudinary__CloudName, Cloudinary__ApiKey and Cloudinary__ApiSecret (Cloudinary's own
// CLOUDINARY_CLOUD_NAME, CLOUDINARY_API_KEY and CLOUDINARY_API_SECRET names work too). Without a
// complete set, every upload is skipped and scans simply have no screenshot.
public sealed class CloudinaryScreenshotStore : IScreenshotStore
{
    private const string Folder = "projectmd/screenshots";

    private readonly Cloudinary? _cloudinary;
    private readonly ILogger<CloudinaryScreenshotStore> _logger;

    // Reads the account settings once; nothing is checked over the network here.
    public CloudinaryScreenshotStore(IConfiguration configuration, ILogger<CloudinaryScreenshotStore> logger)
    {
        _logger = logger;
        _cloudinary = Create(configuration);
        if (_cloudinary is null)
        {
            _logger.LogWarning(
                "Cloudinary is not fully configured (needs a URL, or cloud name + API key + API secret); scans will have no screenshot");
        }
    }

    public async Task<string?> UploadAsync(byte[] jpeg, CancellationToken ct)
    {
        if (_cloudinary is null || jpeg.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(jpeg, writable: false);
            var result = await _cloudinary.UploadAsync(
                new ImageUploadParams { File = new FileDescription("screenshot.jpg", stream), Folder = Folder },
                ct);
            if (result.Error is not null || result.SecureUrl is null)
            {
                _logger.LogWarning("Screenshot upload was refused ({Status}); the scan has no screenshot", result.StatusCode);
                return null;
            }

            return result.SecureUrl.ToString();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Type only: the message could echo the account settings.
            _logger.LogWarning("Screenshot upload failed ({ErrorType}); the scan has no screenshot", ex.GetType().Name);
            return null;
        }
    }

    private static Cloudinary? Create(IConfiguration configuration)
    {
        var url = configuration["Cloudinary:Url"] ?? configuration["CLOUDINARY_URL"];
        if (!string.IsNullOrWhiteSpace(url))
        {
            return new Cloudinary(url) { Api = { Secure = true } };
        }

        var cloud = configuration["Cloudinary:CloudName"] ?? configuration["CLOUDINARY_CLOUD_NAME"];
        var key = configuration["Cloudinary:ApiKey"] ?? configuration["CLOUDINARY_API_KEY"];
        var secret = configuration["Cloudinary:ApiSecret"] ?? configuration["CLOUDINARY_API_SECRET"];
        if (string.IsNullOrWhiteSpace(cloud) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        return new Cloudinary(new Account(cloud, key, secret)) { Api = { Secure = true } };
    }
}
