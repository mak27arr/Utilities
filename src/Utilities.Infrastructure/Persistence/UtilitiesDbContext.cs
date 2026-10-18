namespace Utilities.Infrastructure.Persistence;

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Utilities.Core.Domain.Models;

public sealed class UtilitiesDbContext(DbContextOptions<UtilitiesDbContext> options) : DbContext(options)
{
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Meter> Meters => Set<Meter>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();
    public DbSet<Reading> Readings => Set<Reading>();
    public DbSet<MeterTariffPeriod> MeterTariffPeriods => Set<MeterTariffPeriod>();
    public DbSet<SubscriptionRate> SubscriptionRates => Set<SubscriptionRate>();
    public DbSet<ReminderSettings> ReminderSettings => Set<ReminderSettings>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<NullableUtcDateTimeOffsetConverter>();
        configurationBuilder.Properties<decimal>().HaveConversion<InvariantDecimalConverter>();
        configurationBuilder.Properties<decimal?>().HaveConversion<NullableInvariantDecimalConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureSoftDelete<Address>(modelBuilder);
        ConfigureSoftDelete<Meter>(modelBuilder);
        ConfigureSoftDelete<Subscription>(modelBuilder);
        ConfigureSoftDelete<ServiceType>(modelBuilder);
        ConfigureSoftDelete<Reading>(modelBuilder);
        ConfigureSoftDelete<MeterTariffPeriod>(modelBuilder);
        ConfigureSoftDelete<SubscriptionRate>(modelBuilder);
        ConfigureSoftDelete<ReminderSettings>(modelBuilder);

        modelBuilder.Entity<Address>(entity =>
        {
            entity.Property(item => item.Name).HasMaxLength(160).IsRequired();
            entity.Property(item => item.FullAddress).HasMaxLength(500);
        });
        modelBuilder.Entity<Meter>(entity =>
        {
            entity.Property(item => item.Name).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Unit).HasMaxLength(24).IsRequired();
            entity.Property(item => item.MeterNumber).HasMaxLength(80);
            entity.Property(item => item.AccountNumber).HasMaxLength(80);
            entity.HasIndex(item => item.AddressId);
            entity.HasOne<Address>().WithMany().HasForeignKey(item => item.AddressId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ServiceType>().WithMany().HasForeignKey(item => item.ServiceTypeId).OnDelete(DeleteBehavior.Restrict);
            entity.OwnsOne(item => item.ReadingArea, owned =>
            {
                owned.Property(area => area.X).HasColumnName("ReadingAreaX");
                owned.Property(area => area.Y).HasColumnName("ReadingAreaY");
                owned.Property(area => area.Width).HasColumnName("ReadingAreaWidth");
                owned.Property(area => area.Height).HasColumnName("ReadingAreaHeight");
            });
        });
        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.Property(item => item.Name).HasMaxLength(120).IsRequired();
            entity.HasIndex(item => item.AddressId);
            entity.HasOne<Address>().WithMany().HasForeignKey(item => item.AddressId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ServiceType>().WithMany().HasForeignKey(item => item.ServiceTypeId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ServiceType>(entity =>
        {
            entity.Property(item => item.Name).HasMaxLength(100).IsRequired();
            entity.Property(item => item.DefaultUnit).HasMaxLength(24);
            entity.Property(item => item.IconKey).HasMaxLength(64).IsRequired();
            entity.HasIndex(item => item.Name);
        });
        modelBuilder.Entity<Reading>(entity =>
        {
            entity.HasIndex(item => new { item.MeterId, item.MeasuredAt });
            entity.HasOne<Meter>().WithMany().HasForeignKey(item => item.MeterId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(item => item.RecognizedText).HasMaxLength(1000);
        });
        modelBuilder.Entity<MeterTariffPeriod>(entity =>
        {
            entity.HasIndex(item => new { item.MeterId, item.EffectiveFrom });
            entity.HasOne<Meter>().WithMany().HasForeignKey(item => item.MeterId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<SubscriptionRate>(entity =>
        {
            entity.HasIndex(item => new { item.SubscriptionId, item.EffectiveFrom });
            entity.HasOne<Subscription>().WithMany().HasForeignKey(item => item.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ReminderSettings>().HasIndex(item => new { item.OwnerType, item.OwnerId, item.Kind }).IsUnique();
    }

    private static void ConfigureSoftDelete<TEntity>(ModelBuilder modelBuilder) where TEntity : SoftDeletableEntity
    {
        modelBuilder.Entity<TEntity>().HasKey(item => item.Id);
        modelBuilder.Entity<TEntity>().HasQueryFilter(item => item.DeletedAt == null);
    }
}

public sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, long>(
    value => value.UtcTicks,
    ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

public sealed class NullableUtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset?, long?>(
    value => value.HasValue ? value.Value.UtcTicks : null,
    ticks => ticks.HasValue ? new DateTimeOffset(ticks.Value, TimeSpan.Zero) : null);

public sealed class InvariantDecimalConverter() : ValueConverter<decimal, string>(
    value => value.ToString(CultureInfo.InvariantCulture),
    value => decimal.Parse(value, CultureInfo.InvariantCulture));

public sealed class NullableInvariantDecimalConverter() : ValueConverter<decimal?, string?>(
    value => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : null,
    value => value != null ? decimal.Parse(value, CultureInfo.InvariantCulture) : null);
