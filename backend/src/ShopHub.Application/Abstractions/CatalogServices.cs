namespace ShopHub.Application.Abstractions;

public static class Buckets
{
    public const string Products = "sh-products";
    public const string Reviews = "sh-reviews";
    public const string Kyc = "sh-kyc";
    public const string Chat = "sh-chat";
    public const string Banners = "sh-banners";
    // Return / dispute evidence (photos, videos of the buyer's home, labels…): private, signed links for the parties only
    public const string Returns = "sh-returns";

    public static bool IsPrivate(string bucket) => bucket is Kyc or Chat or Returns;
}

public interface IObjectStorage
{
    Task PutAsync(string bucket, string key, byte[] content, string contentType, CancellationToken ct);
    Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct);

    /// <summary>URL served through the gateway for public buckets.</summary>
    string PublicUrl(string bucket, string key);

    /// <summary>Short-lived signed URL for private buckets (KYC documents, chat photos, return evidence).</summary>
    Task<string> SignedUrlAsync(string bucket, string key, TimeSpan lifetime, CancellationToken ct);

    Task CopyAsync(string fromBucket, string key, string toBucket, CancellationToken ct);

    Task DeleteAsync(string bucket, string key, CancellationToken ct);
}

public record ImageVariant(int MaxSide, byte[] WebP);

public record ProcessedImage(int Width, int Height, IReadOnlyList<ImageVariant> Variants);

/// <summary>Decodes, auto-orients and re-encodes images to WebP (which also drops EXIF/GPS metadata).</summary>
public interface IImageProcessor
{
    /// <exception cref="InvalidDataException">The bytes are not a decodable image.</exception>
    ProcessedImage Process(byte[] data, IReadOnlyList<int> maxSides);
}

public interface IVideoInspector
{
    /// <summary>Duration of an MP4/MOV file in milliseconds, or null when the container cannot be read.</summary>
    int? GetDurationMs(byte[] data);

    /// <summary>
    /// The same video without its metadata (GPS location, device, author, dates — <c>udta</c>, <c>meta</c>, XMP <c>uuid</c>
    /// boxes): each such box is turned into a <c>free</c> box of the same size with zeroed content, so no offset moves.
    /// </summary>
    byte[] StripMetadata(byte[] data);
}

public interface IHtmlSanitizer
{
    string Sanitize(string html);
}

/// <summary>Column-level encryption for sensitive values (bank account, ID card numbers).</summary>
public interface IDataEncryptor
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}
