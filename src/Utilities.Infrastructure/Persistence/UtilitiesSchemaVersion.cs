namespace Utilities.Infrastructure.Persistence;

/// <summary>
/// Runtime schema marker. Increment this whenever a migration is added so startup can
/// compare SQLite's PRAGMA user_version without discovering migrations on every launch.
/// </summary>
public static class UtilitiesSchemaVersion
{
    public const int Current = 8;
}
