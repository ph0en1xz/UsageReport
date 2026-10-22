using System.Data;
using Dapper;
using UsageReport.Api.Database;

namespace UsageReport.Api.Features.MonthlyUsage;

public interface IMonthlyUsageReader
{
    Task<IReadOnlyList<UsageMonth>> GetAsync(int months, CancellationToken cancellationToken);
}

/// <summary>Reads the report from the dbo.GetMonthlyUsage stored procedure, where all aggregation happens.</summary>
/// <remarks>"Now" comes from <see cref="TimeProvider"/>, so tests can pin the report to a fixed date.</remarks>
public sealed class MonthlyUsageReader(IDbConnectionFactory connectionFactory, TimeProvider timeProvider) : IMonthlyUsageReader
{
    public async Task<IReadOnlyList<UsageMonth>> GetAsync(int months, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            "dbo.GetMonthlyUsage",
            new { AsOf = timeProvider.GetUtcNow().UtcDateTime, Months = months },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows
            .Select(r => new UsageMonth(
                r.MonthStart.ToString("yyyy-MM"),
                r.IsPartialMonth,
                r.ActiveCustomers,
                r.ActiveUsers,
                r.TrialsStarted,
                r.TrialsConverted))
            .ToList();
    }

    // Shape of a row as the stored procedure returns it.
    internal sealed record Row(
        DateTime MonthStart,
        bool IsPartialMonth,
        int ActiveCustomers,
        int ActiveUsers,
        int TrialsStarted,
        int TrialsConverted);
}
