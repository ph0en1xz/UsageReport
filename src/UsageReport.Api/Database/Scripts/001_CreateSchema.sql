-- Schema for the usage report.
-- All timestamps are UTC.
-- Every non-key index serves a metric in the report (dbo.GetMonthlyUsage).

-- The paying account (a company). One customer has many users.
CREATE TABLE dbo.Customers
(
    Id         INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
    Name       NVARCHAR(200)      NOT NULL,
    CreatedAt  DATETIME2(0)       NOT NULL,
    PaidFrom   DATETIME2(0)       NULL,  -- NULL = never paid
    PaidUntil  DATETIME2(0)       NULL,  -- NULL = still paying

    CONSTRAINT CK_Customers_PaidWindow CHECK
        (PaidUntil IS NULL OR (PaidFrom IS NOT NULL AND PaidUntil >= PaidFrom))
);

-- A person who logs in under a customer.
CREATE TABLE dbo.Users
(
    Id          INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    CustomerId  INT                NOT NULL CONSTRAINT FK_Users_Customers REFERENCES dbo.Customers (Id),
    Email       NVARCHAR(320)      NOT NULL CONSTRAINT UQ_Users_Email UNIQUE,
    CreatedAt   DATETIME2(0)       NOT NULL
);

-- A customer's free trial. At most one per customer.
CREATE TABLE dbo.Trials
(
    Id           INT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Trials PRIMARY KEY,
    CustomerId   INT                NOT NULL CONSTRAINT FK_Trials_Customers REFERENCES dbo.Customers (Id)
                                             CONSTRAINT UQ_Trials_CustomerId UNIQUE,
    StartedAt    DATETIME2(0)       NOT NULL,
    EndsAt       DATETIME2(0)       NOT NULL,
    ConvertedAt  DATETIME2(0)       NULL,  -- NULL = did not convert to paid

    CONSTRAINT CK_Trials_ConvertsAfterStart CHECK (ConvertedAt IS NULL OR ConvertedAt >= StartedAt)
);

-- "Trials started" per month.
CREATE INDEX IX_Trials_StartedAt ON dbo.Trials (StartedAt);

-- "Trials converted" per month. Filtered: only converted trials are indexed.
CREATE INDEX IX_Trials_ConvertedAt ON dbo.Trials (ConvertedAt) WHERE ConvertedAt IS NOT NULL;

-- One row per user action. The fastest-growing table, hence BIGINT.
CREATE TABLE dbo.UserActivity
(
    Id          BIGINT IDENTITY(1, 1) NOT NULL CONSTRAINT PK_UserActivity PRIMARY KEY,
    UserId      INT                   NOT NULL CONSTRAINT FK_UserActivity_Users REFERENCES dbo.Users (Id),
    OccurredAt  DATETIME2(0)          NOT NULL
);

-- "Active users": distinct users per month, answered from the index alone (covering).
CREATE INDEX IX_UserActivity_OccurredAt ON dbo.UserActivity (OccurredAt) INCLUDE (UserId);
