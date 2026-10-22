using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using UsageReport.Api.Database;
using UsageReport.Api.Features.MonthlyUsage;

namespace UsageReport.Tests;

/// <summary>
/// Runs the real stored procedure against a throwaway database filled with a tiny hand-made dataset,
/// where each row exists to exercise one rule of the metric definitions. Expected numbers are worked out by hand.
/// Needs the SQL Server from docker-compose running (see README).
/// </summary>
public sealed class MonthlyUsageReportTests : IAsyncLifetime
{
    // The report is pinned to 15 March 2026, noon UTC: March is the partial month.
    private static readonly DateTimeOffset AsOf = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    private const string Fixture = """
        SET IDENTITY_INSERT dbo.Customers ON;
        INSERT INTO dbo.Customers (Id, Name, CreatedAt, PaidFrom, PaidUntil) VALUES
            (1, 'Paying since December',          '2025-12-10', '2025-12-10',          NULL),
            (2, 'Churned mid February',           '2026-01-05', '2026-01-05',          '2026-02-20'),
            (3, 'Cancelled exactly on 1 March',   '2026-01-20', '2026-01-20',          '2026-03-01 00:00:00'),
            (4, 'Trial in Jan, converted in Feb', '2026-01-25', '2026-02-03',          NULL),
            (5, 'Trial in Feb, never converted',  '2026-02-10', NULL,                  NULL),
            (6, 'Trial after the as-of date',     '2026-03-20', NULL,                  NULL);
        SET IDENTITY_INSERT dbo.Customers OFF;

        INSERT INTO dbo.Trials (CustomerId, StartedAt, EndsAt, ConvertedAt) VALUES
            (4, '2026-01-25', '2026-02-08', '2026-02-03'),
            (5, '2026-02-10', '2026-02-24', NULL),
            (6, '2026-03-20', '2026-04-03', NULL);

        SET IDENTITY_INSERT dbo.Users ON;
        INSERT INTO dbo.Users (Id, CustomerId, Email, CreatedAt) VALUES
            (1, 1, 'u1@one.example', '2025-12-10'),
            (2, 1, 'u2@one.example', '2025-12-10'),
            (3, 2, 'u3@two.example', '2026-01-05');
        SET IDENTITY_INSERT dbo.Users OFF;

        INSERT INTO dbo.UserActivity (UserId, OccurredAt) VALUES
            (1, '2026-01-05 09:00:00'),  -- u1 active twice in January: counted once
            (1, '2026-01-06 09:00:00'),
            (3, '2026-01-31 23:59:59'),  -- last second of January
            (1, '2026-02-28 23:59:59'),  -- last second of February
            (2, '2026-03-01 00:00:00'),  -- first second of March
            (1, '2026-03-16 09:00:00');  -- after the as-of date: ignored
        """;

    private readonly string _databaseName = $"UsageReport_Test_{Guid.NewGuid():N}";
    private string _connectionString = "";

    [Fact]
    public async Task Report_matches_the_metric_definitions()
    {
        var reader = new MonthlyUsageReader(new SqlConnectionFactory(_connectionString), new FixedTimeProvider(AsOf));

        var report = await reader.GetAsync(months: 4, CancellationToken.None);

        UsageMonth[] expected =
        [
            //       Month      Partial ActiveCustomers ActiveUsers TrialsStarted TrialsConverted
            new("2025-12", false, 1, 0, 0, 0),  // only customer 1 paying; a month with no activity still appears
            new("2026-01", false, 3, 2, 1, 0),  // customers 1-3; u1 (twice) + u3; customer 4's trial starts
            new("2026-02", false, 4, 1, 1, 1),  // 2 churned mid-month still counts; 4 converts (credited to Feb, not Jan)
            new("2026-03", true,  2, 1, 0, 0),  // 3 cancelled at 00:00 on 1 Mar: not active; trial on 20 Mar is after as-of
        ];

        Assert.Equal(expected, report);
    }

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(MonthlyUsageReader).Assembly)  // same secrets as the API, for local runs
            .AddEnvironmentVariables()                            // ConnectionStrings__UsageDb, for CI
            .Build();

        var serverConnectionString = configuration.GetConnectionString("UsageDb")
            ?? throw new InvalidOperationException("Connection string 'UsageDb' is not configured. See README.");

        _connectionString = new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = _databaseName }.ConnectionString;

        DatabaseMigrator.Migrate(_connectionString);

        await using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(Fixture);
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();

        var master = new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.ExecuteAsync($"""
            ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            DROP DATABASE [{_databaseName}];
            """);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
