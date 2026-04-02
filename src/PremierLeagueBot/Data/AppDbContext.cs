using Microsoft.EntityFrameworkCore;
using PremierLeagueBot.Data.Entities;

namespace PremierLeagueBot.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // User
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.TelegramId);
            e.Property(u => u.TelegramId).ValueGeneratedNever();
            e.HasOne(u => u.FavoriteTeam)
             .WithMany(t => t.Followers)
             .HasForeignKey(u => u.FavoriteTeamId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // Team
        modelBuilder.Entity<Team>(e =>
        {
            e.HasKey(t => t.TeamId);
            e.Property(t => t.TeamId).ValueGeneratedNever();
        });

        // Match
        modelBuilder.Entity<Match>(e =>
        {
            e.HasKey(m => m.MatchId);
            e.Property(m => m.MatchId).ValueGeneratedNever();
            e.HasOne(m => m.HomeTeam)
             .WithMany(t => t.HomeMatches)
             .HasForeignKey(m => m.HomeTeamId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.AwayTeam)
             .WithMany(t => t.AwayMatches)
             .HasForeignKey(m => m.AwayTeamId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(m => m.MatchDate);
            e.HasIndex(m => m.Status);
        });

        // Player
        modelBuilder.Entity<Player>(e =>
        {
            e.HasKey(p => p.PlayerId);
            e.Property(p => p.PlayerId).ValueGeneratedNever();
            e.HasOne(p => p.Team)
             .WithMany(t => t.Players)
             .HasForeignKey(p => p.TeamId);
        });

        // NotificationLog
        modelBuilder.Entity<NotificationLog>(e =>
        {
            e.HasKey(n => n.Id);
            e.HasIndex(n => n.TelegramId);
            e.HasIndex(n => n.SentAt);
        });
    }
}
