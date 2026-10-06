using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Ganss.Xss;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using ShopHub.Application.Abstractions;
using ShopHub.Infrastructure.Configuration;
using SkiaSharp;

namespace ShopHub.Infrastructure.Media;

/// <summary>
/// MinIO (S3). Browsers reach objects through the gateway (/s3/… → minio:9000, GET only); signed URLs are generated
/// against the internal endpoint and re-based onto the public URL — the path and query are what the signature covers,
/// and the gateway forwards the original Host so the signature still matches.
/// </summary>
public sealed class MinioObjectStorage : IObjectStorage
{
    private static readonly string[] PublicBuckets = [Buckets.Products, Buckets.Reviews, Buckets.Banners];
    private static readonly string[] AllBuckets = [Buckets.Products, Buckets.Reviews, Buckets.Kyc, Buckets.Chat, Buckets.Banners];

    private readonly IMinioClient _client;
    private readonly string _internalBase;
    private readonly string _publicBase;

    public MinioObjectStorage(ShopHubSettings settings)
    {
        _client = new MinioClient()
            .WithEndpoint(settings.MinioEndpoint)
            .WithCredentials(settings.MinioAccessKey, settings.MinioSecretKey)
            .Build();
        _internalBase = $"http://{settings.MinioEndpoint}";
        _publicBase = settings.MediaPublicUrl.TrimEnd('/');
    }

    public async Task PutAsync(string bucket, string key, byte[] content, string contentType, CancellationToken ct)
    {
        using var stream = new MemoryStream(content, writable: false);
        await _client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(bucket).WithObject(key).WithStreamData(stream).WithObjectSize(content.LongLength).WithContentType(contentType), ct);
    }

    public async Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct)
    {
        try
        {
            await _client.StatObjectAsync(new StatObjectArgs().WithBucket(bucket).WithObject(key), ct);
            return true;
        }
        catch (ObjectNotFoundException)
        {
            return false;
        }
    }

    public string PublicUrl(string bucket, string key) => $"{_publicBase}/{bucket}/{key}";

    public async Task<string> SignedUrlAsync(string bucket, string key, TimeSpan lifetime, CancellationToken ct)
    {
        var signed = await _client.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(bucket).WithObject(key).WithExpiry((int)lifetime.TotalSeconds));
        return signed.StartsWith(_internalBase, StringComparison.OrdinalIgnoreCase) ? _publicBase + signed[_internalBase.Length..] : signed;
    }

    /// <summary>Create missing buckets; public ones allow anonymous GET, private ones (KYC, chat) do not.</summary>
    public async Task EnsureBucketsAsync(CancellationToken ct)
    {
        foreach (var bucket in AllBuckets)
        {
            if (!await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), ct))
                await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket), ct);
            if (PublicBuckets.Contains(bucket))
            {
                var policy = $$"""
                    {"Version":"2012-10-17","Statement":[{"Effect":"Allow","Principal":{"AWS":["*"]},
                    "Action":["s3:GetObject"],"Resource":["arn:aws:s3:::{{bucket}}/*"]}]}
                    """;
                await _client.SetPolicyAsync(new SetPolicyArgs().WithBucket(bucket).WithPolicy(policy), ct);
            }
        }
    }
}

/// <summary>SkiaSharp: decode → honour EXIF orientation → downscale (never upscale) → WebP. Re-encoding drops all metadata.</summary>
public sealed class SkiaImageProcessor : IImageProcessor
{
    private const int MaxPixels = 40_000_000;

    public ProcessedImage Process(byte[] data, IReadOnlyList<int> maxSides)
    {
        using var codec = SKCodec.Create(new MemoryStream(data)) ?? throw new InvalidDataException("Không đọc được ảnh.");
        var info = codec.Info;
        if ((long)info.Width * info.Height > MaxPixels) throw new InvalidDataException("Ảnh quá lớn.");

        using var raw = SKBitmap.Decode(codec) ?? throw new InvalidDataException("Không đọc được ảnh.");
        using var oriented = Orient(raw, codec.EncodedOrigin);

        var variants = new List<ImageVariant>();
        foreach (var maxSide in maxSides)
        {
            var scale = Math.Min(1.0, (double)maxSide / Math.Max(oriented.Width, oriented.Height));
            var w = Math.Max(1, (int)Math.Round(oriented.Width * scale));
            var h = Math.Max(1, (int)Math.Round(oriented.Height * scale));
            using var resized = scale < 1.0 ? oriented.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKCubicResampler.Mitchell)) : oriented.Copy();
            using var image = SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SKEncodedImageFormat.Webp, 82) ?? throw new InvalidDataException("Không mã hoá được ảnh.");
            variants.Add(new ImageVariant(maxSide, encoded.ToArray()));
        }
        return new ProcessedImage(oriented.Width, oriented.Height, variants);
    }

    private static SKBitmap Orient(SKBitmap src, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return src.Copy();
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? src.Height : src.Width, swap ? src.Width : src.Height);
        using var canvas = new SKCanvas(result);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1, src.Width / 2f, 0); break;
            case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180, src.Width / 2f, src.Height / 2f); break;
            case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1, 0, src.Height / 2f); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(result.Width, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(result.Width, 0); canvas.RotateDegrees(90); canvas.Scale(1, -1, 0, src.Height / 2f); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, result.Height); canvas.RotateDegrees(270); break;
            case SKEncodedOrigin.LeftTop: canvas.Translate(0, result.Height); canvas.RotateDegrees(270); canvas.Scale(1, -1, 0, src.Height / 2f); break;
        }
        canvas.DrawBitmap(src, 0, 0);
        return result;
    }
}

/// <summary>Reads the movie duration from an MP4/MOV container (moov → mvhd) without decoding any frame.</summary>
public sealed class Mp4VideoInspector : IVideoInspector
{
    public int? GetDurationMs(byte[] data)
    {
        var moov = FindBox(data, 0, data.Length, "moov");
        if (moov is not { } m) return null;
        var mvhd = FindBox(data, m.Start, m.End, "mvhd");
        if (mvhd is not { } h || h.End - h.Start < 32) return null;

        var span = data.AsSpan(h.Start);
        var version = span[0];
        uint timescale;
        ulong duration;
        if (version == 1)
        {
            timescale = BinaryPrimitives.ReadUInt32BigEndian(span[20..]);
            duration = BinaryPrimitives.ReadUInt64BigEndian(span[24..]);
        }
        else
        {
            timescale = BinaryPrimitives.ReadUInt32BigEndian(span[12..]);
            duration = BinaryPrimitives.ReadUInt32BigEndian(span[16..]);
        }
        if (timescale == 0) return null;
        return (int)Math.Min(int.MaxValue, duration * 1000 / timescale);
    }

    // Returns the payload range (after the box header) of the first child box of the given type
    private static (int Start, int End)? FindBox(byte[] data, int from, int to, string type)
    {
        var pos = from;
        while (pos + 8 <= to)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos));
            var name = Encoding.ASCII.GetString(data, pos + 4, 4);
            var header = 8;
            if (size == 1 && pos + 16 <= to)
            {
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(pos + 8));
                header = 16;
            }
            else if (size == 0)
            {
                size = to - pos;
            }
            if (size < header || pos + size > to) return null;
            if (name == type) return (pos + header, (int)(pos + size));
            pos += (int)size;
        }
        return null;
    }
}

/// <summary>Allow-list HTML sanitiser for product descriptions and CMS pages (no scripts, handlers, iframes, styles).</summary>
public sealed class HtmlSanitizerAdapter : Application.Abstractions.IHtmlSanitizer
{
    private readonly HtmlSanitizer _sanitizer;

    public HtmlSanitizerAdapter()
    {
        _sanitizer = new HtmlSanitizer();
        _sanitizer.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "br", "b", "strong", "i", "em", "u", "ul", "ol", "li", "h2", "h3", "h4", "blockquote", "img", "a", "table", "thead", "tbody", "tr", "th", "td", "span" })
            _sanitizer.AllowedTags.Add(tag);
        _sanitizer.AllowedAttributes.Clear();
        foreach (var attr in new[] { "href", "src", "alt", "title", "colspan", "rowspan" })
            _sanitizer.AllowedAttributes.Add(attr);
        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.AllowedSchemes.Add("https");
        _sanitizer.AllowedSchemes.Add("http");
        _sanitizer.AllowedCssProperties.Clear();
    }

    public string Sanitize(string html) => _sanitizer.Sanitize(html).Trim();
}

/// <summary>AES-256-GCM column encryption. Format: "v1:" + base64(nonce ‖ ciphertext ‖ tag).</summary>
public sealed class AesGcmDataEncryptor(ShopHubSettings settings) : IDataEncryptor
{
    private const string Prefix = "v1:";
    private readonly byte[] _key = settings.DataKey;

    public string Encrypt(string plaintext)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Prefix + Convert.ToBase64String([.. nonce, .. cipher, .. tag]);
    }

    public string Decrypt(string ciphertext)
    {
        if (!ciphertext.StartsWith(Prefix, StringComparison.Ordinal)) throw new CryptographicException("Định dạng dữ liệu mã hoá không hợp lệ.");
        var all = Convert.FromBase64String(ciphertext[Prefix.Length..]);
        var nonceSize = AesGcm.NonceByteSizes.MaxSize;
        var tagSize = AesGcm.TagByteSizes.MaxSize;
        var nonce = all.AsSpan(0, nonceSize);
        var tag = all.AsSpan(all.Length - tagSize);
        var cipher = all.AsSpan(nonceSize, all.Length - nonceSize - tagSize);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, tagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
