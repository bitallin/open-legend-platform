using System.Security.Cryptography;
using Legend.Admin.Data;
using Legend.Admin.Models;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Legend.Admin.Services;

public sealed class MediaLibraryService(VersionPackService packs, AdminDbContext db, AuditService audit)
{
    public const long MaxBytesPerFile = 8 * 1024 * 1024;
    public static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".webp", ".gif" };
    public static readonly string[] Kinds = ["ui", "monster", "item", "map_tile", "sfx_placeholder", "vfx", "other"];

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] JpgMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] GifMagic = [0x47, 0x49, 0x46];
    private static readonly byte[] WebpRiff = [0x52, 0x49, 0x46, 0x46];

    public string AssetsRoot(string packId) => Path.Combine(packs.GetPackRoot(packId), "assets");
    public string LibraryDir(string packId) => Path.Combine(AssetsRoot(packId), "library");
    public string ThumbsDir(string packId) => Path.Combine(AssetsRoot(packId), "thumbs");

    public Task EnsureDirsAsync(string packId, CancellationToken ct = default)
    {
        Directory.CreateDirectory(LibraryDir(packId));
        Directory.CreateDirectory(ThumbsDir(packId));
        Directory.CreateDirectory(Path.Combine(AssetsRoot(packId), "ui"));
        Directory.CreateDirectory(Path.Combine(AssetsRoot(packId), "monsters"));
        Directory.CreateDirectory(Path.Combine(AssetsRoot(packId), "items"));
        Directory.CreateDirectory(Path.Combine(AssetsRoot(packId), "maps"));
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<MediaAsset>> QueryAsync(string packId, string? kind, string? q, bool includeDeleted = false, CancellationToken ct = default)
    {
        var query = db.MediaAssets.AsQueryable().Where(a => a.PackId == packId);
        if (!includeDeleted) query = query.Where(a => !a.SoftDeleted);
        if (!string.IsNullOrWhiteSpace(kind) && kind != "all")
            query = query.Where(a => a.Kind == kind);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(a => a.FileName.Contains(term) || a.Tags.Contains(term) || a.Id.Contains(term));
        }
        var list = await query.Take(500).ToListAsync(ct);
        return list.OrderByDescending(a => a.CreatedAt).ToList();
    }

    public async Task<MediaAsset?> FindAsync(string id, CancellationToken ct = default)
        => await db.MediaAssets.FindAsync([id], ct);

    public async Task<(bool ok, string message, MediaAsset? asset)> UploadAsync(
        string packId, string kind, string tags, IFormFile file, string actor, string ip, CancellationToken ct = default)
    {
        if (file.Length <= 0) return (false, "空文件", null);
        if (file.Length > MaxBytesPerFile) return (false, $"超过 {MaxBytesPerFile / 1024 / 1024}MB 限制", null);
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext) || !AllowedExt.Contains(ext))
            return (false, "仅支持 PNG/JPG/WebP/GIF", null);
        if (!Kinds.Contains(kind)) kind = "other";

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        if (!LooksLikeImage(bytes, ext))
            return (false, "文件头不是合法图片", null);

        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var existing = await db.MediaAssets.FirstOrDefaultAsync(a => a.PackId == packId && a.Sha256 == sha && !a.SoftDeleted, ct);
        if (existing is not null)
        {
            await audit.WriteAsync(actor, "media.upload.dedupe", existing.Id, new { existing.FileName, sha }, ip, ct);
            return (true, "已存在相同内容，复用素材", existing);
        }

        await EnsureDirsAsync(packId, ct);
        var id = Guid.NewGuid().ToString("N");
        var safeExt = ext.ToLowerInvariant();
        if (safeExt == ".jpeg") safeExt = ".jpg";
        var rel = $"assets/library/{id}{safeExt}";
        var abs = Path.Combine(packs.GetPackRoot(packId), rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        await File.WriteAllBytesAsync(abs, bytes, ct);

        int w = 0, h = 0;
        string thumbRel = "";
        try
        {
            using var bitmap = SKBitmap.Decode(bytes);
            if (bitmap is not null)
            {
                w = bitmap.Width;
                h = bitmap.Height;
                int tw = 160, th = 160;
                float scale = Math.Min(tw / (float)w, th / (float)h);
                int nw = Math.Max(1, (int)(w * scale));
                int nh = Math.Max(1, (int)(h * scale));
                using var resized = bitmap.Resize(new SKImageInfo(nw, nh), SKFilterQuality.Medium);
                if (resized is not null)
                {
                    using var image = SKImage.FromBitmap(resized);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 90);
                    thumbRel = $"assets/thumbs/{id}.png";
                    var thumbAbs = Path.Combine(packs.GetPackRoot(packId), thumbRel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(thumbAbs)!);
                    await using var fs = File.OpenWrite(thumbAbs);
                    data.SaveTo(fs);
                }
            }
        }
        catch
        {
            // non-fatal
        }

        var contentType = safeExt switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => file.ContentType
        };

        var row = new MediaAsset
        {
            Id = id,
            PackId = packId,
            Kind = kind,
            FileName = Path.GetFileName(file.FileName),
            RelPath = rel,
            ThumbRelPath = thumbRel,
            ContentType = contentType,
            Bytes = bytes.LongLength,
            Width = w,
            Height = h,
            Sha256 = sha,
            Tags = tags ?? "",
            SoftDeleted = false,
            CreatedBy = actor,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.MediaAssets.Add(row);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(actor, "media.upload", id, new { row.FileName, row.Kind, row.Bytes, row.RelPath }, ip, ct);
        return (true, "上传成功", row);
    }

    public async Task<(bool ok, string message)> SoftDeleteAsync(string id, string actor, string ip, CancellationToken ct = default)
    {
        var row = await db.MediaAssets.FindAsync([id], ct);
        if (row is null) return (false, "不存在");
        row.SoftDeleted = true;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(actor, "media.delete", id, new { row.FileName, row.RelPath }, ip, ct);
        return (true, "已删除（软删，磁盘文件保留）");
    }

    public string? ResolveAbsolute(MediaAsset asset)
    {
        try
        {
            var root = packs.GetPackRoot(asset.PackId);
            var abs = Path.GetFullPath(Path.Combine(root, asset.RelPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!abs.StartsWith(root, StringComparison.Ordinal)) return null;
            return File.Exists(abs) ? abs : null;
        }
        catch { return null; }
    }

    public string? ResolveThumbAbsolute(MediaAsset asset)
    {
        if (string.IsNullOrEmpty(asset.ThumbRelPath)) return ResolveAbsolute(asset);
        try
        {
            var root = packs.GetPackRoot(asset.PackId);
            var abs = Path.GetFullPath(Path.Combine(root, asset.ThumbRelPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!abs.StartsWith(root, StringComparison.Ordinal)) return null;
            return File.Exists(abs) ? abs : ResolveAbsolute(asset);
        }
        catch { return null; }
    }

    private static bool LooksLikeImage(byte[] bytes, string ext)
    {
        if (bytes.Length < 12) return false;
        if (StartsWith(bytes, PngMagic)) return true;
        if (StartsWith(bytes, JpgMagic)) return true;
        if (StartsWith(bytes, GifMagic)) return true;
        if (StartsWith(bytes, WebpRiff) && bytes.Length > 11 &&
            bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
            return true;
        return AllowedExt.Contains(ext);
    }

    private static bool StartsWith(byte[] data, byte[] prefix)
    {
        if (data.Length < prefix.Length) return false;
        for (int i = 0; i < prefix.Length; i++)
            if (data[i] != prefix[i]) return false;
        return true;
    }
}
