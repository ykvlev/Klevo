using Microsoft.EntityFrameworkCore;

namespace Klevo.Core.Data;

public class KlevoDbContext(DbContextOptions<KlevoDbContext> options) : DbContext(options)
{
    public DbSet<FisheryBasin> Basins => Set<FisheryBasin>();
    public DbSet<FisheryZone> Zones => Set<FisheryZone>();
    public DbSet<FishSpecies> Species => Set<FishSpecies>();
    public DbSet<ZoneSizeRule> SizeRules => Set<ZoneSizeRule>();
    public DbSet<ZoneLimitRule> LimitRules => Set<ZoneLimitRule>();
    public DbSet<ZoneDefaultLimit> DefaultLimits => Set<ZoneDefaultLimit>();
    public DbSet<ZoneBan> Bans => Set<ZoneBan>();
    public DbSet<Spot> Spots => Set<Spot>();
    public DbSet<WeatherObservation> WeatherObservations => Set<WeatherObservation>();
    public DbSet<SolunarDay> SolunarDays => Set<SolunarDay>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Spot>(e =>
        {
            e.Property(s => s.Location).HasColumnType("geography");
        });
        modelBuilder.Entity<ZoneSizeRule>(e =>
        {
            e.HasKey(r => new { r.ZoneId, r.SpeciesId });
            e.HasOne(r => r.Species)
                .WithMany()
                .HasForeignKey(r => r.SpeciesId);
        });

        modelBuilder.Entity<ZoneLimitRule>(e =>
        {
            e.HasKey(r => new { r.ZoneId, r.SpeciesId });
            e.HasOne(r => r.Species)
                .WithMany()
                .HasForeignKey(r => r.SpeciesId);
        });

        modelBuilder.Entity<ZoneBan>(e =>
        {
            e.HasOne(b => b.Species)
                .WithMany()
                .HasForeignKey(b => b.SpeciesId);
        });

        modelBuilder.Entity<WeatherObservation>(e =>
        {
            e.HasIndex(o => new { o.SpotId, o.ObservedAt }).IsUnique();
        });

        modelBuilder.Entity<SolunarDay>(e =>
        {
            e.HasKey(d => new { d.SpotId, d.Date });
        });
    }
}

