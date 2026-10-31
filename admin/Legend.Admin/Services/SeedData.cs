using Legend.Admin.Data;
using Legend.Admin.Models;
using Microsoft.EntityFrameworkCore;

namespace Legend.Admin.Services;

public static class SeedData
{
    public static async Task EnsureSeedAsync(AdminDbContext db)
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
