using DbUp;

namespace UsageReport.Api.Database;

/// <summary>
/// Creates the database if missing and applies any SQL scripts under Database/Scripts that have not run yet.
/// DbUp records applied scripts in dbo.SchemaVersions, so each script runs exactly once.
/// </summary>
public static class DatabaseMigrator
{
    public static void Migrate(string connectionString)
    {
        EnsureDatabase.For.SqlDatabase(connectionString);

        var result = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(DatabaseMigrator).Assembly)
            .WithTransactionPerScript()
            .LogToConsole()
            .Build()
            .PerformUpgrade();

        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }
    }
}
