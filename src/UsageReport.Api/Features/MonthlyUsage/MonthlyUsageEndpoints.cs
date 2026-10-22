namespace UsageReport.Api.Features.MonthlyUsage;

public static class MonthlyUsageEndpoints
{
    private const int DefaultMonths = 12;
    private const int MaxMonths = 36;

    public static void MapMonthlyUsageEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/reports/monthly-usage?months=12
        app.MapGet("/api/reports/monthly-usage", async (int? months, IMonthlyUsageReader reader, CancellationToken cancellationToken) =>
        {
            var monthCount = months ?? DefaultMonths;
            if (monthCount is < 1 or > MaxMonths)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["months"] = [$"Must be between 1 and {MaxMonths}."],
                });
            }

            return Results.Ok(await reader.GetAsync(monthCount, cancellationToken));
        });
    }
}
