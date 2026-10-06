namespace ShopHub.Application.Abstractions;

public static class Buckets
{
    public const string Products = "sh-products";
    public const string Reviews = "sh-reviews";
    public const string Kyc = "sh-kyc";
    public const string Chat = "sh-chat";
    public const string Banners = "sh-banners";
}

public interface IObjectStorage
{
    Task PutAsync(string bucket, string key, byte[] content, string contentType, CancellationToken ct);
    Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct);

    /// <summary>URL served through the gateway for public buckets.</summary>
    string PublicUrl(string bucket, string key);

    /// <summary>Short-lived signed URL for private buckets (KYC documents).</summary>
    Task<string> SignedUrlAsync(string bucket, string key, TimeSpan lifetime, CancellationToken ct);
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
