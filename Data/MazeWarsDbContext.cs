using MazeWars.GameServer.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MazeWars.GameServer.Data;

public class MazeWarsDbContext : DbContext
{
    public MazeWarsDbContext(DbContextOptions<MazeWarsDbContext> options) : base(options)
    {
    }

    public DbSet<PlayerAccount> Players { get; set; } = null!;
    public DbSet<StashedItem> StashedItems { get; set; } = null!;
    public DbSet<MatchRecord> MatchHistory { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlayerAccount>(entity =>
        {
            entity.HasIndex(e => e.PlayerName).IsUnique();
            entity.HasMany(e => e.StashedItems)
                  .WithOne(e => e.Player)
                  .HasForeignKey(e => e.PlayerAccountId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.MatchHistory)
                  .WithOne(e => e.Player)
                  .HasForeignKey(e => e.PlayerAccountId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
