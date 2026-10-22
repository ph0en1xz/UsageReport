-- Monthly usage report: one row per calendar month (UTC), oldest first.
--
-- @AsOf    the "now" of the report. Defaults to the current time; tests pass a fixed value.
-- @Months  how many months to return, ending with the month that contains @AsOf.
--
-- The month containing @AsOf is partial: it only counts up to @AsOf and is flagged IsPartialMonth = 1.
--
-- Definitions (all intervals are half-open: start inclusive, end exclusive):
--   ActiveCustomers  customers whose paid window [PaidFrom, PaidUntil) overlaps the month
--   ActiveUsers      distinct users with at least one activity in the month
--   TrialsStarted    trials whose StartedAt falls in the month
--   TrialsConverted  trials whose ConvertedAt falls in the month (credited to the conversion month)
CREATE OR ALTER PROCEDURE dbo.GetMonthlyUsage
    @AsOf   DATETIME2(0) = NULL,
    @Months INT          = 12
AS
BEGIN
    SET NOCOUNT ON;

    IF @Months NOT BETWEEN 1 AND 36
        THROW 50001, 'Months must be between 1 and 36.', 1;

    SET @AsOf = COALESCE(@AsOf, SYSUTCDATETIME());

    DECLARE @CurrentMonthStart DATETIME2(0) = DATEFROMPARTS(YEAR(@AsOf), MONTH(@AsOf), 1);

    -- 1. The calendar: one row per month, so months with no activity still appear (as zeros).
    --    GENERATE_SERIES gives 0, 1, 2 ... = how many months back from the current month.
    WITH Months AS -- Common Table Expression (CTE) named Months
    (
        SELECT
            n.value                                          AS MonthsBack,
            DATEADD(MONTH, -n.value, @CurrentMonthStart)     AS MonthStart
        FROM GENERATE_SERIES(0, @Months - 1) AS n
    ),
    Periods AS
    (
        SELECT
            MonthStart,
            -- A month ends at the start of the next month, or at @AsOf if that comes first (the current month).
            LEAST(DATEADD(MONTH, 1, MonthStart), @AsOf)      AS PeriodEnd,
            CAST(IIF(MonthsBack = 0, 1, 0) AS BIT)           AS IsPartialMonth
        FROM Months
    )

    -- 2. For each month, count the four metrics. CROSS APPLY with COUNT always returns a row, so zero is 0, not missing.
    SELECT
        CAST(p.MonthStart AS DATE) AS MonthStart,
        p.IsPartialMonth,
        activeCustomers.Total      AS ActiveCustomers,
        activeUsers.Total          AS ActiveUsers,
        trialsStarted.Total        AS TrialsStarted,
        trialsConverted.Total      AS TrialsConverted
    FROM Periods AS p
    CROSS APPLY
    (
        SELECT COUNT(*) AS Total
        FROM dbo.Customers AS c
        WHERE c.PaidFrom < p.PeriodEnd
          AND (c.PaidUntil IS NULL OR c.PaidUntil > p.MonthStart)
    ) AS activeCustomers
    CROSS APPLY
    (
        SELECT COUNT(DISTINCT a.UserId) AS Total
        FROM dbo.UserActivity AS a
        WHERE a.OccurredAt >= p.MonthStart
          AND a.OccurredAt <  p.PeriodEnd
    ) AS activeUsers
    CROSS APPLY
    (
        SELECT COUNT(*) AS Total
        FROM dbo.Trials AS t
        WHERE t.StartedAt >= p.MonthStart
          AND t.StartedAt <  p.PeriodEnd
    ) AS trialsStarted
    CROSS APPLY
    (
        SELECT COUNT(*) AS Total
        FROM dbo.Trials AS t
        WHERE t.ConvertedAt >= p.MonthStart
          AND t.ConvertedAt <  p.PeriodEnd
    ) AS trialsConverted
    ORDER BY p.MonthStart;
END;
