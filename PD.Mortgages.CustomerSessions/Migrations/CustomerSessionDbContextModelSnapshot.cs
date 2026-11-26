using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace PD.Mortgages.CustomerSessions.Migrations;

[DbContext(typeof(CustomerSessionDbContext))]
public sealed class CustomerSessionDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 128);

        modelBuilder.Entity("PD.Mortgages.CustomerSessions.CustomerSessionRecord", entity =>
        {
            entity.Property<string>("Id").HasMaxLength(449).HasColumnType("nvarchar(449)");
            entity.Property<DateTimeOffset?>("AbsoluteExpiration").HasColumnType("datetimeoffset");
            entity.Property<DateTimeOffset>("ExpiresAtTime").HasColumnType("datetimeoffset");
            entity.Property<long?>("SlidingExpirationInSeconds").HasColumnType("bigint");
            entity.Property<byte[]>("Value").IsRequired().HasColumnType("varbinary(max)");
            entity.HasKey("Id");
            entity.HasIndex("ExpiresAtTime");
            entity.ToTable("MortgageSessionCache", "dbo");
        });
    }
}
