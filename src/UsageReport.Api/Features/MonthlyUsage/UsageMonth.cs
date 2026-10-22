namespace UsageReport.Api.Features.MonthlyUsage;

/// <summary>One month of the usage report. <see cref="Month"/> is "yyyy-MM" (UTC).</summary>
public sealed record UsageMonth(
    string Month,
    bool IsPartialMonth,
    int ActiveCustomers,
    int ActiveUsers,
    int TrialsStarted,
    int TrialsConverted);
