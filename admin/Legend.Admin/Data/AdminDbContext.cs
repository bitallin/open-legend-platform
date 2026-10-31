using Legend.Admin.Models;
using Microsoft.EntityFrameworkCore;

namespace Legend.Admin.Data;

public sealed class AdminDbContext : DbContext
{
    public AdminDbContext(DbContextOptions<AdminDbContext> options) : base(options) { }

    public DbSet<AdminUser> Users => Set<AdminUser>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ConfigRevision> ConfigRevisions => Set<ConfigRevision>();
    public DbSet<MailJob> MailJobs => Set<MailJob>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<CdkBatch> CdkBatches => Set<CdkBatch>();
    public DbSet<CdkCode> CdkCodes => Set<CdkCode>();
    public DbSet<MergePlan> MergePlans => Set<MergePlan>();
    public DbSet<CurrencyAdjustment> CurrencyAdjustments => Set<CurrencyAdjustment>();
    public DbSet<AssetManifestRecord> AssetManifests => Set<AssetManifestRecord>();
    public DbSet<RechargeOrderNote> RechargeOrders => Set<RechargeOrderNote>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminUser>().HasIndex(x => x.Username).IsUnique();
        modelBuilder.Entity<CdkCode>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<CdkCode>().HasOne(x => x.Batch).WithMany(x => x.Codes).HasForeignKey(x => x.BatchId);
        modelBuilder.Entity<AuditLog>().HasIndex(x => x.CreatedAt);
        modelBuilder.Entity<ConfigRevision>().HasIndex(x => new { x.PackId, x.RelativePath, x.CreatedAt });
        modelBuilder.Entity<MediaAsset>().HasKey(x => x.Id);
        modelBuilder.Entity<MediaAsset>().HasIndex(x => new { x.PackId, x.Kind, x.SoftDeleted });
        modelBuilder.Entity<MediaAsset>().HasIndex(x => x.Sha256);
    }
}
