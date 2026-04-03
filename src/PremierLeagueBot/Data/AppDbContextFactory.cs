using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PremierLeagueBot.Data;

/// <summary>
/// Design-time factory used by EF Core tools (dotnet-ef migrations add ...).
/// Not used at runtime.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=design_time.db")
            .Options;
        return new AppDbContext(opts);
    }
}
