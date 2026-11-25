using Microsoft.EntityFrameworkCore;

namespace PD.Mortgages.Infrastructure.Persistence;

public sealed class MortgageDbContext(DbContextOptions<MortgageDbContext> options) : DbContext(options)
{
    public DbSet<MortgageApplicationRecord> Applications => Set<MortgageApplicationRecord>();
    public DbSet<MortgageApplicationAuditRecord> AuditEntries => Set<MortgageApplicationAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MortgageApplicationRecord>(entity =>
        {
            entity.ToTable("MortgageApplications");
            entity.HasKey(application => application.Id);
            entity.Property(application => application.IdSearchText).HasMaxLength(36).IsRequired();
            entity.Property(application => application.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(application => application.CreatedByEmployeeId).HasMaxLength(256);
            entity.Property(application => application.FirstName).HasMaxLength(200);
            entity.Property(application => application.LastName).HasMaxLength(200);
            entity.Property(application => application.Email).HasMaxLength(320);
            entity.Property(application => application.SnapshotJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(application => application.Version).IsConcurrencyToken();
            entity.HasIndex(application => new { application.Status, application.UpdatedAtUtc });
            entity.HasIndex(application => application.CreatedByEmployeeId);
        });

        modelBuilder.Entity<MortgageApplicationAuditRecord>(entity =>
        {
            entity.ToTable("MortgageApplicationAuditEntries");
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.ActorId).HasMaxLength(256).IsRequired();
            entity.Property(entry => entry.Action).HasMaxLength(512).IsRequired();
            entity.HasIndex(entry => new { entry.ApplicationId, entry.RecordedAtUtc, entry.Id });
            entity.HasOne<MortgageApplicationRecord>()
                .WithMany()
                .HasForeignKey(entry => entry.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    }
}
