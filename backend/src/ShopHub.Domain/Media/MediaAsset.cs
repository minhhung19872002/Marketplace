using ShopHub.Domain.Common;

namespace ShopHub.Domain.Media;

public enum MediaKind
{
    Image,
    Video,
    Document,
}

/// <summary>
/// An uploaded file after validation (byte signature, size), re-encoding (images: WebP in three sizes, metadata
/// stripped) and storage in MinIO. Private kinds (KYC) are only reachable through signed, expiring URLs.
/// </summary>
public class MediaAsset : Entity
{
    private MediaAsset() { }

    public MediaAsset(Guid ownerUserId, MediaKind kind, string purpose, string bucket, string objectKey, string contentType,
        long sizeBytes, int? width, int? height, int? durationMs, DateTimeOffset createdAt)
    {
        OwnerUserId = ownerUserId;
        Kind = kind;
        Purpose = purpose;
        Bucket = bucket;
        ObjectKey = objectKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Width = width;
        Height = height;
        DurationMs = durationMs;
        CreatedAt = createdAt;
    }

    public Guid OwnerUserId { get; private set; }
    public MediaKind Kind { get; private set; }

    // product | avatar | kyc | shop | review | chat | banner
    public string Purpose { get; private set; } = string.Empty;
    public string Bucket { get; private set; } = string.Empty;

    /// <summary>The objects were moved to another bucket (same keys).</summary>
    public void MoveTo(string bucket) => Bucket = bucket;

    // Images: base key; the stored objects are {key}_1200.webp, {key}_600.webp, {key}_200.webp
    public string ObjectKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public int? Width { get; private set; }
    public int? Height { get; private set; }
    public int? DurationMs { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
