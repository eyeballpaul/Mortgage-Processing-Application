using Microsoft.EntityFrameworkCore;

namespace PD.Notifications.Worker;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<SimulatedNotificationRecord> SimulatedNotifications => Set<SimulatedNotificationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SimulatedNotificationRecord>(entity =>
        {
            entity.ToTable("SimulatedNotifications");
            entity.HasKey(entry => entry.EventId);
        });
    }
}

public sealed class SimulatedNotificationRecord
{
    public Guid EventId { get; set; }
    public Guid ApplicationId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}
