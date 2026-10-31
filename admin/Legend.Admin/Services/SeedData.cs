using System.Text.Json;
using Legend.Admin.Data;
using Legend.Admin.Models;
using Microsoft.EntityFrameworkCore;

namespace Legend.Admin.Services;

public static class SeedData
{
    public static async Task EnsureSeedAsync(AdminDbContext db, RepoPaths? paths = null)
    {
        await db.Database.EnsureCreatedAsync();
        await EnsureMediaAssetsTableAsync(db);
        if (!await db.Users.AnyAsync())
        {
            db.Users.Add(new AdminUser
            {
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("LegendAdmin!2026"),
                Role = AdminRoles.SuperAdmin,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
            db.Users.Add(new AdminUser
            {
                Username = "operator",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("LegendOps!2026"),
                Role = AdminRoles.Operator,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
            db.Users.Add(new AdminUser
            {
                Username = "support",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("LegendSupport!2026"),
                Role = AdminRoles.Support,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        if (!await db.Announcements.AnyAsync())
        {
            db.Announcements.Add(new Announcement
            {
                Title = "欢迎使用 Open Legend 运营后台",
                Body = "本后台用于配置版本包、区服、爆率、商城、邮件与在线运维。默认开发账号见 docs/admin。",
                IsActive = true,
                Actor = "seed",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        if (!await db.AssetManifests.AnyAsync())
        {
            db.AssetManifests.Add(new AssetManifestRecord
            {
                Name = "ui-shell-default",
                Kind = "ui",
                VersionTag = "1.0.0",
                EntriesJson = """[{"id":"login_bg","path":"assets/ui/login_bg.png"},{"id":"hud_frame","path":"assets/ui/hud_frame.png"}]""",
                Notes = "对齐 docs/site 19；由运营替换真实资源路径",
                Actor = "seed",
                UpdatedAt = DateTimeOffset.UtcNow
            });
            db.AssetManifests.Add(new AssetManifestRecord
            {
                Name = "sfx-core",
                Kind = "sfx",
                VersionTag = "1.0.0",
                EntriesJson = """[{"id":"attack_swing","path":"assets/sfx/attack_swing.wav"},{"id":"loot_pickup","path":"assets/sfx/loot_pickup.wav"}]""",
                Notes = "对齐 docs/site 20",
                Actor = "seed",
                UpdatedAt = DateTimeOffset.UtcNow
            });
            db.AssetManifests.Add(new AssetManifestRecord
            {
                Name = "vfx-core",
                Kind = "vfx",
                VersionTag = "1.0.0",
                EntriesJson = """[{"id":"hit_spark","path":"assets/vfx/hit_spark.json"},{"id":"level_up","path":"assets/vfx/level_up.json"}]""",
                Notes = "特效索引，客户端按 id 加载",
                Actor = "seed",
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        if (!await db.RechargeOrders.AnyAsync())
        {
            db.RechargeOrders.Add(new RechargeOrderNote
            {
                OrderId = "seed-order-001",
                AccountId = "demo-account",
                CharacterName = "演示角色",
                AmountCents = 600,
                CurrencyId = "yuanbao",
                GrantAmount = 60,
                Status = "Manual",
                Provider = "manual",
                Actor = "seed",
                Note = "种子补单示例（未真正入账 Host）",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();

        if (paths is not null)
            await SyncMediaFromPackManifestAsync(db, paths, "sample-classic");
    }

    /// <summary>Import MediaAssets rows from version-packs/{pack}/configs/assets_manifest.json when files exist on disk.</summary>
    public static async Task SyncMediaFromPackManifestAsync(AdminDbContext db, RepoPaths paths, string packId)
    {
        var manifestPath = Path.Combine(paths.VersionPacksRoot, packId, "configs", "assets_manifest.json");
        if (!File.Exists(manifestPath)) return;

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        if (!doc.RootElement.TryGetProperty("assets", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        var existing = await db.MediaAssets.Where(a => a.PackId == packId).ToDictionaryAsync(a => a.Id);
        var now = DateTimeOffset.UtcNow;
        var packRoot = Path.Combine(paths.VersionPacksRoot, packId);
        var changed = false;

        foreach (var el in arr.EnumerateArray())
        {
            var id = el.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var rel = el.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(rel)) continue;

            var abs = Path.GetFullPath(Path.Combine(packRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!abs.StartsWith(Path.GetFullPath(packRoot), StringComparison.Ordinal))
                continue;
            if (!File.Exists(abs))
            {
                // Materialize from committed seed-media when library was wiped / fresh clone.
                var seedRel = el.TryGetProperty("seedPath", out var seedEl) ? seedEl.GetString() : null;
                if (!string.IsNullOrWhiteSpace(seedRel))
                {
                    var seedAbs = Path.GetFullPath(Path.Combine(packRoot, seedRel.Replace('/', Path.DirectorySeparatorChar)));
                    if (seedAbs.StartsWith(Path.GetFullPath(packRoot), StringComparison.Ordinal) && File.Exists(seedAbs))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
                        File.Copy(seedAbs, abs, overwrite: true);
                        var thumbRel0 = el.TryGetProperty("thumb", out var th0) ? th0.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(thumbRel0))
                        {
                            var thumbAbs = Path.GetFullPath(Path.Combine(packRoot, thumbRel0.Replace('/', Path.DirectorySeparatorChar)));
                            if (thumbAbs.StartsWith(Path.GetFullPath(packRoot), StringComparison.Ordinal))
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(thumbAbs)!);
                                File.Copy(seedAbs, thumbAbs, overwrite: true);
                            }
                        }
                    }
                }
            }
            if (!File.Exists(abs))
                continue;

            var thumb = el.TryGetProperty("thumb", out var thEl) ? (thEl.GetString() ?? "") : "";
            var kind = el.TryGetProperty("kind", out var kEl) ? (kEl.GetString() ?? "other") : "other";
            var fileName = el.TryGetProperty("fileName", out var fnEl) ? (fnEl.GetString() ?? Path.GetFileName(rel)) : Path.GetFileName(rel);
            var contentType = el.TryGetProperty("contentType", out var ctEl) ? (ctEl.GetString() ?? "image/png") : "image/png";
            var bytes = el.TryGetProperty("bytes", out var bEl) && bEl.TryGetInt64(out var bl) ? bl : new FileInfo(abs).Length;
            var width = el.TryGetProperty("width", out var wEl) && wEl.TryGetInt32(out var wi) ? wi : 0;
            var height = el.TryGetProperty("height", out var hEl) && hEl.TryGetInt32(out var hi) ? hi : 0;
            var sha = el.TryGetProperty("sha256", out var sEl) ? (sEl.GetString() ?? "") : "";
            var tags = el.TryGetProperty("tags", out var tEl) ? (tEl.GetString() ?? "") : "";

            if (existing.TryGetValue(id, out var row))
            {
                row.Kind = kind;
                row.FileName = fileName!;
                row.RelPath = rel!;
                row.ThumbRelPath = thumb;
                row.ContentType = contentType!;
                row.Bytes = bytes;
                row.Width = width;
                row.Height = height;
                row.Sha256 = sha!;
                row.Tags = tags!;
                row.SoftDeleted = false;
                row.UpdatedAt = now;
                changed = true;
            }
            else
            {
                db.MediaAssets.Add(new MediaAsset
                {
                    Id = id!,
                    PackId = packId,
                    Kind = kind!,
                    FileName = fileName!,
                    RelPath = rel!,
                    ThumbRelPath = thumb,
                    ContentType = contentType!,
                    Bytes = bytes,
                    Width = width,
                    Height = height,
                    Sha256 = sha!,
                    Tags = tags!,
                    SoftDeleted = false,
                    CreatedBy = "seed-classic-default-assets",
                    CreatedAt = now,
                    UpdatedAt = now
                });
                changed = true;
            }
        }

        if (changed) await db.SaveChangesAsync();
    }

    private static async Task EnsureMediaAssetsTableAsync(AdminDbContext db)
    {
        // EnsureCreated does not add new tables to an existing SQLite file.
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "MediaAssets" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_MediaAssets" PRIMARY KEY,
                "PackId" TEXT NOT NULL,
                "Kind" TEXT NOT NULL,
                "FileName" TEXT NOT NULL,
                "RelPath" TEXT NOT NULL,
                "ThumbRelPath" TEXT NOT NULL,
                "ContentType" TEXT NOT NULL,
                "Bytes" INTEGER NOT NULL,
                "Width" INTEGER NOT NULL,
                "Height" INTEGER NOT NULL,
                "Sha256" TEXT NOT NULL,
                "Tags" TEXT NOT NULL,
                "SoftDeleted" INTEGER NOT NULL,
                "CreatedBy" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_MediaAssets_PackId_Kind_SoftDeleted" ON "MediaAssets" ("PackId", "Kind", "SoftDeleted");""");
        await db.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_MediaAssets_Sha256" ON "MediaAssets" ("Sha256");""");
    }
}
