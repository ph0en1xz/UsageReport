using Dapper;
using UsageReport.Api.Database;

namespace UsageReport.Api.Features.SampleData;

/// <summary>
/// Development-only: generates sample data and writes it in a single transaction.
/// Does nothing if the database already has customers, so restarts don't duplicate data.
/// </summary>
public sealed class SampleDataSeeder(IDbConnectionFactory connectionFactory, ILogger<SampleDataSeeder> logger)
{
    // Fixed random seed: same seed + same date = same data.
    private const int RandomSeed = 42;

    // The generator assigns Ids so foreign keys line up, hence IDENTITY_INSERT.
    // Dapper runs each INSERT once per row in the list.
    private const string InsertCustomers = """
        SET IDENTITY_INSERT dbo.Customers ON;
        INSERT INTO dbo.Customers (Id, Name, CreatedAt, PaidFrom, PaidUntil)
        VALUES (@Id, @Name, @CreatedAt, @PaidFrom, @PaidUntil);
        SET IDENTITY_INSERT dbo.Customers OFF;
        """;

    private const string InsertUsers = """
        SET IDENTITY_INSERT dbo.Users ON;
        INSERT INTO dbo.Users (Id, CustomerId, Email, CreatedAt)
        VALUES (@Id, @CustomerId, @Email, @CreatedAt);
        SET IDENTITY_INSERT dbo.Users OFF;
        """;

    private const string InsertTrials = """
        SET IDENTITY_INSERT dbo.Trials ON;
        INSERT INTO dbo.Trials (Id, CustomerId, StartedAt, EndsAt, ConvertedAt)
        VALUES (@Id, @CustomerId, @StartedAt, @EndsAt, @ConvertedAt);
        SET IDENTITY_INSERT dbo.Trials OFF;
        """;

    private const string InsertActivity = """
        INSERT INTO dbo.UserActivity (UserId, OccurredAt)
        VALUES (@UserId, @OccurredAt);
        """;

    /// <summary>Seeds sample history ending at <paramref name="asOfUtc"/>, if the database is empty.</summary>
    public async Task SeedAsync(DateTime asOfUtc, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var alreadySeeded = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.Customers) THEN 1 ELSE 0 END",
            cancellationToken: cancellationToken));

        if (alreadySeeded == 1)
        {
            logger.LogInformation("Sample data already present; skipping seed.");
            return;
        }

        var data = new SampleDataGenerator(RandomSeed).Generate(asOfUtc);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await Insert(InsertCustomers, data.Customers);
        await Insert(InsertUsers, data.Users);
        await Insert(InsertTrials, data.Trials);
        await Insert(InsertActivity, data.Activity);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Seeded {Customers} customers, {Users} users, {Trials} trials, {Activity} activity rows (as of {AsOf:u}).",
            data.Customers.Count, data.Users.Count, data.Trials.Count, data.Activity.Count, asOfUtc);

        Task Insert<TRow>(string sql, IEnumerable<TRow> rows) =>
            connection.ExecuteAsync(new CommandDefinition(sql, rows, transaction, cancellationToken: cancellationToken));
    }
}
