using UsageReport.Api.Database;
using UsageReport.Api.Features.MonthlyUsage;
using UsageReport.Api.Features.SampleData;

var builder = WebApplication.CreateBuilder(args);

// The password is never in source: supplied via user-secrets (local) or an environment variable.
var connectionString = builder.Configuration.GetConnectionString("UsageDb")
    ?? throw new InvalidOperationException("Connection string 'UsageDb' is not configured. See README.");

builder.Services.AddSingleton<IDbConnectionFactory>(new SqlConnectionFactory(connectionString));
// Whenever a class asks for a TimeProvider, hand it the actual, real-world hardware clock
// instead of reaching out to a global static clock.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IMonthlyUsageReader, MonthlyUsageReader>();
builder.Services.AddScoped<SampleDataSeeder>();

// Errors (400/404/500) are returned as standard RFC 7807 problem+json, never as stack traces.
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// The dashboard: wwwroot/index.html, served at "/".
app.UseDefaultFiles();
app.UseStaticFiles();

DatabaseMigrator.Migrate(connectionString);

// Sample data is for local development only; never seed a real environment.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<SampleDataSeeder>();
    await seeder.SeedAsync(DateTime.UtcNow, CancellationToken.None);
}

app.MapMonthlyUsageEndpoints();

app.Run();
