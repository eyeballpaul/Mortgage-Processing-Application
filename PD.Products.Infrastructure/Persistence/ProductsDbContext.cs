using Microsoft.EntityFrameworkCore;
using PD.Products.Domain.Entities;

namespace PD.Products.Infrastructure.Persistence;

public sealed class ProductsDbContext(DbContextOptions<ProductsDbContext> options) : DbContext(options)
{
    public DbSet<MortgageProduct> Products => Set<MortgageProduct>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MortgageProduct>(entity =>
        {
            entity.ToTable("MortgageProducts");
            entity.HasKey(product => product.Id);
            entity.Property(product => product.Id).HasMaxLength(64).IsRequired();
            entity.Property(product => product.Name).HasMaxLength(200).IsRequired();
            entity.Property(product => product.InterestRate).HasPrecision(9, 4);
            entity.Property(product => product.Fee).HasPrecision(18, 2);
            entity.Property(product => product.Aprc).HasPrecision(9, 4);
            entity.Property(product => product.FixedTermYears).IsRequired();
            entity.Property(product => product.MaximumLoanToValue).IsRequired();
            entity.HasData(MortgageProductSeedData.Products.ToArray());
        });
    }
}
