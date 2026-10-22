using UsageReport.Api.Features.SampleData;

namespace UsageReport.Tests;

public sealed class SampleDataGeneratorTests
{
    private static readonly DateTime AsOf = new(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Same_seed_produces_the_same_data()
    {
        var first = new SampleDataGenerator(seed: 42).Generate(AsOf);
        var second = new SampleDataGenerator(seed: 42).Generate(AsOf);

        Assert.Equal(first.Customers, second.Customers);
        Assert.Equal(first.Activity, second.Activity);
    }

    [Fact]
    public void Nothing_happens_after_the_as_of_date()
    {
        var data = new SampleDataGenerator(seed: 42).Generate(AsOf);

        Assert.All(data.Trials, t => Assert.True(t.StartedAt <= AsOf));
        Assert.All(data.Trials, t => Assert.True(t.ConvertedAt is null || t.ConvertedAt <= AsOf));
        Assert.All(data.Activity, a => Assert.True(a.OccurredAt <= AsOf));
    }

    [Fact]
    public void Converted_trial_customers_start_paying_when_they_convert()
    {
        var data = new SampleDataGenerator(seed: 42).Generate(AsOf);
        var customers = data.Customers.ToDictionary(c => c.Id);

        Assert.All(data.Trials, t => Assert.Equal(t.ConvertedAt, customers[t.CustomerId].PaidFrom));
    }
}
