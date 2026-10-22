namespace UsageReport.Api.Features.SampleData;

public sealed record CustomerRow(int Id, string Name, DateTime CreatedAt, DateTime? PaidFrom, DateTime? PaidUntil);
public sealed record UserRow(int Id, int CustomerId, string Email, DateTime CreatedAt);
public sealed record TrialRow(int Id, int CustomerId, DateTime StartedAt, DateTime EndsAt, DateTime? ConvertedAt);
public sealed record ActivityRow(int UserId, DateTime OccurredAt);

public sealed record SampleDataSet(
    IReadOnlyList<CustomerRow> Customers,
    IReadOnlyList<UserRow> Users,
    IReadOnlyList<TrialRow> Trials,
    IReadOnlyList<ActivityRow> Activity);

/// <summary>
/// Builds a reproducible B2B SaaS history in memory (no database access).
/// Customers arrive either through a trial (which may convert to paid) or by buying directly.
/// Paying customers may churn. Each customer has users, and users generate activity while the customer is live.
/// </summary>
public sealed class SampleDataGenerator(int seed)
{
    private const int HistoryMonths = 15;              // 12 reported + 3 lead-in, so month 1 already has paying customers
    private const int BaseTrialsPerMonth = 15;
    private const int BaseDirectPurchasesPerMonth = 3;
    private const int MaxUsersPerPayingCustomer = 5;
    private const int MaxUsersPerTrialCustomer = 2;
    private const double MonthlyGrowth = 0.04;
    private const int TrialLengthDays = 14;
    private const double TrialConversionRate = 0.25;
    private const double MonthlyChurnRate = 0.03;
    private const double UserGoesDormantRate = 0.20;

    private readonly Random _random = new(seed);
    private readonly List<CustomerRow> _customers = [];
    private readonly List<UserRow> _users = [];
    private readonly List<TrialRow> _trials = [];
    private readonly List<ActivityRow> _activity = [];

    public SampleDataSet Generate(DateTime asOfUtc)
    {
        var currentMonth = new DateTime(asOfUtc.Year, asOfUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var historyStart = currentMonth.AddMonths(-(HistoryMonths - 1));

        for (var month = 0; month < HistoryMonths; month++)
        {
            var monthStart = historyStart.AddMonths(month);
            var monthEnd = Min(monthStart.AddMonths(1), asOfUtc);

            // The current month is partial: scale volumes by how much of it has elapsed.
            var elapsedFraction = (monthEnd - monthStart) / (monthStart.AddMonths(1) - monthStart);
            var growth = Math.Pow(1 + MonthlyGrowth, month) * elapsedFraction;

            for (var i = 0; i < AroundValue(BaseTrialsPerMonth * growth); i++)
            {
                AddTrialCustomer(RandomTime(monthStart, monthEnd), asOfUtc);
            }

            for (var i = 0; i < AroundValue(BaseDirectPurchasesPerMonth * growth); i++)
            {
                AddDirectCustomer(RandomTime(monthStart, monthEnd), asOfUtc);
            }
        }

        return new SampleDataSet(_customers, _users, _trials, _activity);
    }

    private void AddTrialCustomer(DateTime startedAt, DateTime asOfUtc)
    {
        var endsAt = startedAt.AddDays(TrialLengthDays);

        DateTime? convertedAt = null;
        if (_random.NextDouble() < TrialConversionRate)
        {
            var conversion = RandomTime(startedAt.AddDays(1), endsAt);
            convertedAt = conversion <= asOfUtc ? conversion : null;   // can't convert in the future
        }

        var customer = AddCustomer(createdAt: startedAt, paidFrom: convertedAt, asOfUtc);
        _trials.Add(new TrialRow(_trials.Count + 1, customer.Id, startedAt, endsAt, convertedAt));

        // Non-converted customers only use the product during the trial.
        var liveUntil = convertedAt is null ? Min(endsAt, asOfUtc) : customer.PaidUntil ?? asOfUtc;
        var userCount = convertedAt is null
            ? _random.Next(1, MaxUsersPerTrialCustomer + 1)
            : _random.Next(1, MaxUsersPerPayingCustomer + 1);
        AddUsers(customer, userCount, liveUntil);
    }

    private void AddDirectCustomer(DateTime purchasedAt, DateTime asOfUtc)
    {
        var customer = AddCustomer(createdAt: purchasedAt, paidFrom: purchasedAt, asOfUtc);
        AddUsers(customer, _random.Next(1, MaxUsersPerPayingCustomer + 1), customer.PaidUntil ?? asOfUtc);
    }

    private CustomerRow AddCustomer(DateTime createdAt, DateTime? paidFrom, DateTime asOfUtc)
    {
        var id = _customers.Count + 1;
        var paidUntil = paidFrom is null ? null : ChurnTime(paidFrom.Value, asOfUtc);
        var customer = new CustomerRow(id, $"Customer {id:D4}", createdAt, paidFrom, paidUntil);
        _customers.Add(customer);
        return customer;
    }

    /// <summary>Each month a paying customer has a fixed chance of cancelling. Null = still paying.</summary>
    private DateTime? ChurnTime(DateTime paidFrom, DateTime asOfUtc)
    {
        for (var monthStart = paidFrom; monthStart < asOfUtc; monthStart = monthStart.AddMonths(1))
        {
            if (_random.NextDouble() < MonthlyChurnRate)
            {
                return RandomTime(monthStart, Min(monthStart.AddMonths(1), asOfUtc));
            }
        }

        return null;
    }

    private void AddUsers(CustomerRow customer, int count, DateTime customerLiveUntil)
    {
        for (var i = 0; i < count; i++)
        {
            var id = _users.Count + 1;
            var createdAt = RandomTime(customer.CreatedAt, Min(customer.CreatedAt.AddDays(7), customerLiveUntil));
            _users.Add(new UserRow(id, customer.Id, $"user{id}@customer{customer.Id}.example", createdAt));

            // Some users stop using the product while their company keeps paying.
            var activeUntil = _random.NextDouble() < UserGoesDormantRate
                ? RandomTime(createdAt, customerLiveUntil)
                : customerLiveUntil;

            AddActivity(id, createdAt, activeUntil);
        }
    }

    /// <summary>On each day the user may log activity, with a per-user engagement level (2%–50% of days).</summary>
    private void AddActivity(int userId, DateTime from, DateTime until)
    {
        var engagement = 0.02 + _random.NextDouble() * 0.48;

        for (var day = from.Date; day < until; day = day.AddDays(1))
        {
            if (_random.NextDouble() >= engagement)
            {
                continue;
            }

            var windowStart = Max(day, from);
            var windowEnd = Min(day.AddDays(1), until);
            if (windowEnd > windowStart)
            {
                _activity.Add(new ActivityRow(userId, RandomTime(windowStart, windowEnd)));
            }
        }
    }

    /// <summary>A uniformly random time in [from, to), truncated to whole seconds to match DATETIME2(0).</summary>
    private DateTime RandomTime(DateTime from, DateTime to)
    {
        if (to <= from)
        {
            return from;
        }

        var seconds = (long)((to - from).TotalSeconds * _random.NextDouble());
        return from.AddSeconds(seconds);
    }

    /// <summary>Expected value ±20%, so monthly volumes aren't suspiciously smooth.</summary>
    private int AroundValue(double expected) => (int)Math.Round(expected * (0.8 + _random.NextDouble() * 0.4));

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
