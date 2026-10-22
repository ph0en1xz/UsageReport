# Usage Report

A small usage report for a B2B SaaS product: four monthly numbers for the last 12 months, aggregated in SQL Server,
served by an ASP.NET Core API and shown on a one-page dashboard.

![stack](https://img.shields.io/badge/.NET-8-512BD4) ![db](https://img.shields.io/badge/SQL%20Server-2022-CC2927)

## 1. How to run it

**Prerequisites:** .NET 8 SDK, Docker, and (for debugging) VS Code with the C# Dev Kit extension.

Commands are for PowerShell, run from the repository root.

> **Docker inside WSL** (no Docker Desktop): prefix every `docker` command with `wsl`, e.g. `wsl docker compose up -d --wait`,
> and keep a WSL terminal open while working. WSL shuts down idle VMs, which stops the database.

**Step 1: set the database password**

```powershell
Copy-Item .env.example .env
```

Edit `.env` and set `MSSQL_SA_PASSWORD`. SQL Server requires 8+ characters with upper case, lower case, a digit and a
symbol. Avoid `$`, `"` and `;`.

**Step 2: start SQL Server**

```powershell
docker compose up -d --wait (add `wsl` at the begging of the command in my case - no Desktop Docker)
```

Returns once the database reports healthy. The first run downloads the SQL Server image (~1.7 GB).

**Step 3: give the API its connection string**, using the same password as in `.env`. It is stored in your user
profile (.NET user-secrets), never in the repository:

```powershell
dotnet user-secrets set "ConnectionStrings:UsageDb" "Server=127.0.0.1,1433;Database=UsageReport;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True" --project src/UsageReport.Api
```

Use `127.0.0.1` rather than `localhost`: on Windows `localhost` can resolve to IPv6 first, which Docker-in-WSL doesn't forward.

**Step 4: run the API**

- **VS Code:** press **F5**. It builds, starts the API and opens the dashboard.
- **Terminal:** `dotnet run --project src/UsageReport.Api`

On first start the API creates the database and schema and seeds 12+ months of sample data (Development only).

| | |
|---|---|
| Dashboard | http://localhost:5018 |
| API requests | `src/UsageReport.Api/UsageReport.Api.http` (VS Code REST Client, Visual Studio, Rider) |
| Tests | `dotnet test` (needs the database from step 2) |
| Stop the database | `docker compose stop` (keeps data) |
| Reset the data | `docker compose down -v`, then run the API again; sample data is regenerated relative to today |

**Next time:** step 2, then step 4.

**If something goes wrong**

| Error | Fix |
|---|---|
| `Connection string 'UsageDb' is not configured` | Do step 3 |
| `Login failed for user 'sa'` | The password in step 3 must match the one the database was created with. Re-run step 3; if it persists, `docker compose down -v` and step 2 |
| Connection timeout / refused | The database isn't running: step 2 (with WSL, keep a WSL terminal open) |
| `file is locked by .NET Host` when building | The API is still running; stop it first |

## 2. How each number is defined

All times are **UTC**. A month runs from the 1st at 00:00 up to, **not including**, the 1st of the next month,
so nothing is counted in two months.

A **customer** is the paying account (a company); a **user** is a person who logs in under it.

| Number | Definition |
|---|---|
| **Active customers** | Customers whose paid window overlaps the month, i.e. paying at any point in it. Trials don't count. A customer who cancels on the 20th still counts for that month; one who cancels at exactly 00:00 on the 1st does not count for that month. |
| **Active users** | Distinct users with at least one activity in the month. Includes users of trial customers. A user active 30 times counts once. |
| **Trials started** | Trials whose start falls in the month. |
| **Trials converted to paid** | Trials whose conversion falls in the month, **credited to the month of conversion**, not the month the trial started. A trial started 25 Jan and converted 3 Feb counts in February. This answers "what happened this month" and keeps past months stable; the cohort view is listed under section 3. |

**Window:** 12 months ending with the current month. The current month is **partial** (month to date): it is
flagged `isPartialMonth` in the API and faded on the dashboard, so its lower numbers aren't misread as a drop.
Months with no activity still appear, as zeros.

The aggregation is one stored procedure,
[`dbo.GetMonthlyUsage`](src/UsageReport.Api/Database/Scripts/002_CreateGetMonthlyUsage.sql): it builds the list of
months first, then counts each metric per month.

## 3. What I left out

**Data model**
- **One paid period per customer.** Cancel-and-come-back can't be represented. Next step: a `Subscriptions` table (one row per paid period); the overlap query stays the same.
- No plans, pricing or revenue (MRR); at most one trial per customer.

**Metrics**
- No **cohort conversion view** (conversion rate of trials *started* in a month); useful for funnel analysis, but recent cohorts look low until their trials finish.
- No filters (plan, region, customer size); UTC only.

**Scale**
- Counts run on raw tables, which is fine at this size. With millions of activity rows I'd add a daily rollup (`UserActivityDaily`), cache closed months (they never change), or move reporting to a read replica/warehouse.

**Production readiness**
- No authentication/authorization on the API.
- The app connects as `sa` locally; production would use a least-privilege, read-only reporting login.
- The API isn't containerised (compose runs only the database); no CI/CD, structured logging, health checks or monitoring.
- Tests: one SQL integration test plus generator checks. Next: endpoint tests, and Testcontainers so CI needs no pre-started database.

**API & dashboard**
- No drill-down endpoints (e.g. "the trials behind this month's number", "a customer with its users"); they'd make each number verifiable from the dashboard.
- No date-range picker; Chart.js is loaded from a CDN, so the dashboard needs internet access.

---

## Endpoint

`GET /api/reports/monthly-usage?months=12` returns one row per month with the four numbers (`months` 1–36, default 12):

```json
{ "month": "2026-09", "isPartialMonth": false, "activeCustomers": 103, "activeUsers": 296, "trialsStarted": 26, "trialsConverted": 4 }
```

Invalid input returns `400` as `application/problem+json`.

## Data model

```
Customers 1 ──< Users 1 ──< UserActivity
    1
    └──1 Trials
```

| Table | Purpose |
|---|---|
| `Customers` | The account. `PaidFrom` / `PaidUntil` = the window it was paying (`PaidUntil` NULL = still paying) |
| `Users` | People under a customer |
| `Trials` | At most one per customer. `ConvertedAt` NULL = did not convert |
| `UserActivity` | One row per user action |

Schema: [`001_CreateSchema.sql`](src/UsageReport.Api/Database/Scripts/001_CreateSchema.sql). Each index serves one metric in the report.

## Key decisions

- **SQL Server + Dapper, no EF Core.** The brief puts the aggregation in SQL, so the SQL stays visible and reviewable.
- **Stored procedure with an `@AsOf` parameter.** "Now" is injected (.NET 8 `TimeProvider`), so the report can be pinned to a date and tested deterministically.
- **Schema as versioned SQL scripts (DbUp).** Applied at startup, each exactly once; changes go in a new numbered script.
- **One project, organised by feature** (`Features/MonthlyUsage`, `Features/SampleData`). The report feature is a record (the JSON shape), a reader behind an interface (calls the SQL), and a thin endpoint.
- **No CQRS / MediatR / microservices.** The API is read-only and small; a read/write split is worth it once real writes exist.
- **Sample data generated in code, not a static file.** It's always relative to today, reproducible (fixed seed), and the rules (growth, 25% conversion, 3% monthly churn) are readable constants. Seeding only runs in Development.
- **No secrets in the repo.** The DB password lives in `.env` (gitignored); the connection string in user-secrets or an environment variable.

## Tests

- `MonthlyUsageReportTests`: runs the real stored procedure on a throwaway database with a tiny hand-made dataset (6 customers, 3 users) and checks every number against values worked out by hand. It covers the edge cases above: conversion across a month boundary, cancellation at exactly midnight, repeat activity, boundary seconds, events after "now", and a month with no activity.
- `SampleDataGeneratorTests`: the generator is reproducible and internally consistent.

## How I used AI

I built this with an AI coding assistant, working step by step and reviewing each step before moving on.

- **What the AI did:** scaffolding, boilerplate (DI wiring, Dapper mapping, DbUp setup), the sample-data generator, and first drafts of the SQL, dashboard and tests.
- **What I decided:** the metric definitions and their edge cases, the B2B data model, the schema simplifications (one paid window per customer), and the scope, including what to leave out: CQRS, MediatR, microservices, extra endpoints, large sample data.
- **How I checked it:** the SQL test's expected numbers are worked out by hand, not taken from the code's output; I cross-checked the report against the raw trial rows; and running it end to end surfaced two environment issues the AI hadn't anticipated (on Windows `localhost` resolved to IPv6, which Docker-in-WSL doesn't forward, so the connection string uses `127.0.0.1`; and WSL stopping idle VMs, hence `restart: unless-stopped`).
