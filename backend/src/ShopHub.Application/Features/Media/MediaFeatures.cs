using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Media;

namespace ShopHub.Application.Features.Media;

public enum MediaPurpose
{
    Product,
    Avatar,
    Shop,
    Kyc,
}

public record MediaAssetDto(Guid Id, MediaKind Kind, string? Url, string? ThumbnailUrl, int? Width, int? Height, int? DurationMs);

/// <summary>What the bytes actually are (magic numbers), regardless of the declared Content-Type or file name.</summary>
public enum SniffedType
{
    Unknown,
    Jpeg,
    Png,
    Gif,
    WebP,
    Mp4,
    Pdf,
}

public static class FileSignature
{
    public static SniffedType Sniff(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return SniffedType.Jpeg;
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return SniffedType.Png;
        if (b.Length >= 6 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8') return SniffedType.Gif;
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8)) return SniffedType.WebP;
        if (b.Length >= 12 && b[4..8].SequenceEqual("ftyp"u8)) return SniffedType.Mp4;
        if (b.Length >= 5 && b[..5].SequenceEqual("%PDF-"u8)) return SniffedType.Pdf;
        return SniffedType.Unknown;
    }

    public static bool IsImage(SniffedType t) => t is SniffedType.Jpeg or SniffedType.Png or SniffedType.Gif or SniffedType.WebP;
}

/// <summary>Sizes produced for every public image (longest side, px).</summary>
public static class ImageSizes
{
    public const int Large = 1200;
    public const int Medium = 600;
    public const int Small = 200;
    public const int Document = 1600;
    public static readonly IReadOnlyList<int> Public = [Large, Medium, Small];

    public static string Key(string baseKey, int size) => $"{baseKey}_{size}.webp";
}

public record UploadMediaCommand(byte[] Data, MediaPurpose Purpose) : IRequest<MediaAssetDto>;

public sealed class UploadMediaValidator : AbstractValidator<UploadMediaCommand>
{
    public UploadMediaValidator() =>
        RuleFor(x => x.Data).NotEmpty().WithMessage("Tệp rỗng.");
}

public sealed class UploadMediaHandler(
    IApplicationDbContext db,
    IObjectStorage storage,
    IImageProcessor images,
    IVideoInspector videos,
    ISystemParameters parameters,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<UploadMediaCommand, MediaAssetDto>
{
    public async Task<MediaAssetDto> Handle(UploadMediaCommand request, CancellationToken ct)
    {
        var ownerId = currentUser.UserId ?? throw new AuthenticationFailedException("Bạn cần đăng nhập để tiếp tục.");
        var data = request.Data;
        var type = FileSignature.Sniff(data);
        var now = clock.UtcNow;
        var purpose = request.Purpose.ToString().ToLowerInvariant();
        var baseKey = $"{purpose}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}";
        var maxImageBytes = request.Purpose == MediaPurpose.Avatar
            ? 1L * 1024 * 1024
            : await parameters.GetIntAsync(ParameterKeys.MediaMaxImageMb, ct) * 1024 * 1024;

        MediaAsset asset;
        if (FileSignature.IsImage(type))
        {
            if (data.LongLength > maxImageBytes) throw Invalid($"Ảnh tối đa {maxImageBytes / 1024 / 1024} MB.");
            var bucket = request.Purpose == MediaPurpose.Kyc ? Buckets.Kyc : Buckets.Products;
            var sizes = request.Purpose == MediaPurpose.Kyc ? new[] { ImageSizes.Document } : ImageSizes.Public;

            ProcessedImage processed;
            try
            {
                processed = images.Process(data, sizes);
            }
            catch (InvalidDataException)
            {
                throw Invalid("Không đọc được ảnh. Vui lòng chọn tệp JPG, PNG, GIF hoặc WebP hợp lệ.");
            }
            if (processed.Width < 100 || processed.Height < 100) throw Invalid("Ảnh phải có kích thước tối thiểu 100×100 px.");

            foreach (var v in processed.Variants)
                await storage.PutAsync(bucket, ImageSizes.Key(baseKey, v.MaxSide), v.WebP, "image/webp", ct);
            asset = new MediaAsset(ownerId, MediaKind.Image, purpose, bucket, baseKey, "image/webp",
                processed.Variants.Sum(v => (long)v.WebP.Length), processed.Width, processed.Height, null, now);
        }
        else if (type == SniffedType.Mp4 && request.Purpose == MediaPurpose.Product)
        {
            var maxBytes = await parameters.GetIntAsync(ParameterKeys.MediaMaxVideoMb, ct) * 1024 * 1024;
            var maxSeconds = await parameters.GetIntAsync(ParameterKeys.MediaMaxVideoSeconds, ct);
            if (data.LongLength > maxBytes) throw Invalid($"Video tối đa {maxBytes / 1024 / 1024} MB.");
            var duration = videos.GetDurationMs(data) ?? throw Invalid("Không đọc được video. Vui lòng dùng tệp MP4.");
            if (duration > maxSeconds * 1000) throw Invalid($"Video tối đa {maxSeconds} giây.");

            var key = $"{baseKey}.mp4";
            await storage.PutAsync(Buckets.Products, key, data, "video/mp4", ct);
            asset = new MediaAsset(ownerId, MediaKind.Video, purpose, Buckets.Products, key, "video/mp4", data.LongLength, null, null, duration, now);
        }
        else if (type == SniffedType.Pdf && request.Purpose == MediaPurpose.Kyc)
        {
            if (data.LongLength > maxImageBytes) throw Invalid($"Tệp tối đa {maxImageBytes / 1024 / 1024} MB.");
            var key = $"{baseKey}.pdf";
            await storage.PutAsync(Buckets.Kyc, key, data, "application/pdf", ct);
            asset = new MediaAsset(ownerId, MediaKind.Document, purpose, Buckets.Kyc, key, "application/pdf", data.LongLength, null, null, null, now);
        }
        else
        {
            throw Invalid(request.Purpose switch
            {
                MediaPurpose.Product => "Chỉ nhận ảnh (JPG, PNG, GIF, WebP) hoặc video MP4.",
                MediaPurpose.Kyc => "Chỉ nhận ảnh (JPG, PNG, WebP) hoặc PDF.",
                _ => "Chỉ nhận ảnh JPG, PNG, GIF hoặc WebP.",
            });
        }

        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync(ct);
        return MediaUrls.ToDto(asset, storage);
    }

    private static ValidationException Invalid(string message) => new([new ValidationFailure("file", message)]);
}

public static class MediaUrls
{
    public static MediaAssetDto ToDto(MediaAsset a, IObjectStorage storage) => a.Kind switch
    {
        // Private documents never get a public URL
        _ when a.Bucket == Buckets.Kyc => new(a.Id, a.Kind, null, null, a.Width, a.Height, a.DurationMs),
        MediaKind.Image => new(a.Id, a.Kind, storage.PublicUrl(a.Bucket, ImageSizes.Key(a.ObjectKey, ImageSizes.Large)),
            storage.PublicUrl(a.Bucket, ImageSizes.Key(a.ObjectKey, ImageSizes.Small)), a.Width, a.Height, null),
        _ => new(a.Id, a.Kind, storage.PublicUrl(a.Bucket, a.ObjectKey), null, null, null, a.DurationMs),
    };

    /// <summary>Load assets uploaded by this user for the given purpose; anything else is "not found".</summary>
    public static async Task<Dictionary<Guid, MediaAsset>> LoadOwnedAsync(IApplicationDbContext db, Guid ownerId, string purpose,
        IEnumerable<Guid> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        var assets = await db.MediaAssets.Where(a => wanted.Contains(a.Id) && a.OwnerUserId == ownerId && a.Purpose == purpose)
            .ToDictionaryAsync(a => a.Id, ct);
        if (assets.Count != wanted.Count) throw new NotFoundException("Không tìm thấy tệp đã tải lên (hoặc tệp không thuộc về bạn).");
        return assets;
    }
}

// ---------- Avatar ----------

public record SetAvatarCommand(Guid AssetId) : IRequest<string>;

public sealed class SetAvatarHandler(IApplicationDbContext db, IObjectStorage storage, ICurrentUser currentUser)
    : IRequestHandler<SetAvatarCommand, string>
{
    public async Task<string> Handle(SetAvatarCommand request, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException("Bạn cần đăng nhập để tiếp tục.");
        var asset = (await MediaUrls.LoadOwnedAsync(db, userId, "avatar", [request.AssetId], ct))[request.AssetId];
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException();
        var url = storage.PublicUrl(asset.Bucket, ImageSizes.Key(asset.ObjectKey, ImageSizes.Medium));
        user.SetAvatar(url);
        await db.SaveChangesAsync(ct);
        return url;
    }
}
