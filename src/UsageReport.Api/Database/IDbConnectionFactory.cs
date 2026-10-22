using System.Data.Common;

namespace UsageReport.Api.Database;

/// <summary>Opens connections to the usage database. Classes depend on this, not on SQL Server directly.</summary>
public interface IDbConnectionFactory
{
    Task<DbConnection> OpenAsync(CancellationToken cancellationToken);
}
