using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PD.Mortgages.Infrastructure.Persistence;

public sealed class MortgageDbContextFactory : IDesignTimeDbContextFactory<MortgageDbContext>
{
    public MortgageDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__mortgages")
            ?? throw new InvalidOperationException("Set ConnectionStrings__mortgages when creating or applying migrations.");

        var options = new DbContextOptionsBuilder<MortgageDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new MortgageDbContext(options);
    }
}
