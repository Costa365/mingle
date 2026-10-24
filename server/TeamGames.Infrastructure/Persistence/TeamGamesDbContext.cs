using Microsoft.EntityFrameworkCore;
using TeamGames.Core.Rooms;
using TeamGames.Infrastructure.Rooms;

namespace TeamGames.Infrastructure.Persistence;

public sealed class TeamGamesDbContext(DbContextOptions<TeamGamesDbContext> options) : DbContext(options)
{
    public DbSet<Installation> Installations => Set<Installation>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<GuestSession> Guests => Set<GuestSession>();
    public DbSet<CommandReceipt> Receipts => Set<CommandReceipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Room>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => r.Code).IsUnique();
            entity.Property(r => r.Version).IsConcurrencyToken();
            entity.HasMany(r => r.Members).WithOne().HasForeignKey(m => m.RoomId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Membership>(entity =>
        {
            entity.HasKey(m => m.Id);
            entity.HasIndex(m => new { m.RoomId, m.GuestId }).IsUnique();
            entity.HasIndex(m => new { m.RoomId, m.NormalizedName }).IsUnique().HasFilter("\"Removed\" = 0");
        });
        modelBuilder.Entity<GuestSession>().HasKey(g => g.Id);
        modelBuilder.Entity<CommandReceipt>(entity =>
        {
            entity.HasKey(r => new { r.GuestId, r.CommandId });
            entity.HasOne<Room>().WithMany().HasForeignKey(r => r.RoomId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Installation>(entity =>
        {
            entity.ToTable("Installations");
            entity.HasKey(installation => installation.Id);
            entity.Property(installation => installation.Id).ValueGeneratedNever();
        });
    }
}
