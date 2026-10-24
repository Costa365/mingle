using Microsoft.EntityFrameworkCore;

namespace TeamGames.Infrastructure.Persistence;

public sealed class TeamGamesDbContext(DbContextOptions<TeamGamesDbContext> options) : DbContext(options)
{
    public DbSet<Installation> Installations => Set<Installation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Installation>(entity =>
        {
            entity.ToTable("Installations");
            entity.HasKey(installation => installation.Id);
            entity.Property(installation => installation.Id).ValueGeneratedNever();
        });
    }
}
