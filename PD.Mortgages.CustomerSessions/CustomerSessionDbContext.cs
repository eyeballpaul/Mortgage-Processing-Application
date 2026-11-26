using Microsoft.EntityFrameworkCore;

namespace PD.Mortgages.CustomerSessions;

public sealed class CustomerSessionDbContext(DbContextOptions<CustomerSessionDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerSessionRecord>(entity =>
        {
            entity.ToTable("MortgageSessionCache", "dbo");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.Id).HasMaxLength(449);
            entity.Property(session => session.Value).IsRequired();
            entity.HasIndex(session => session.ExpiresAtTime);
        });
    }
}

public sealed class CustomerSessionRecord
{
    public required string Id { get; set; }
    public required byte[] Value { get; set; }
    public DateTimeOffset ExpiresAtTime { get; set; }
    public long? SlidingExpirationInSeconds { get; set; }
    public DateTimeOffset? AbsoluteExpiration { get; set; }
}
