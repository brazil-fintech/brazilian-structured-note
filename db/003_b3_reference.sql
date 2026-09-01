/* --------------------------------------------------------------------------
   B3's published reference data.

   Every table here is a projection of an export under reference/b3/ and is
   fully reloaded by the ingestion worker: nothing is hand-maintained, and a
   refresh is dropping in a newer file. That is also why reloads truncate
   rather than merge — the export is the whole truth, so a row that disappears
   from it must disappear here.
   -------------------------------------------------------------------------- */

IF SCHEMA_ID('b3') IS NULL EXEC('CREATE SCHEMA b3');
GO

/* The first shape of ref.Underlying carried a hand-written seed keyed on Code
   alone. B3 lists an asset once per valuation index, so the grain is wider. */
IF OBJECT_ID('ref.Underlying') IS NOT NULL AND COL_LENGTH('ref.Underlying', 'ValuationIndex') IS NULL
    DROP TABLE ref.Underlying;
GO

IF OBJECT_ID('ref.Underlying') IS NULL
CREATE TABLE ref.Underlying
(
    AssetClass     NVARCHAR(40)  NOT NULL,
    Code           NVARCHAR(60)  NOT NULL,
    ValuationIndex NVARCHAR(160) NOT NULL,
    Exchange       NVARCHAR(60)  NULL,
    Currency       NVARCHAR(60)  NULL,
    Ticker         NVARCHAR(60)  NULL,
    Calculated     BIT           NOT NULL CONSTRAINT DF_Underlying_Calculated DEFAULT 0,
    IsActive       BIT           NOT NULL CONSTRAINT DF_Underlying_IsActive DEFAULT 1,
    CONSTRAINT PK_Underlying PRIMARY KEY (AssetClass, Code, ValuationIndex)
);
GO

/* The picker asks for the codes of one class, which this covers without touching the table. */
IF IndexProperty(OBJECT_ID('ref.Underlying'), 'IX_Underlying_Class', 'IndexId') IS NULL
CREATE INDEX IX_Underlying_Class ON ref.Underlying (AssetClass, Code) INCLUDE (ValuationIndex, Currency);
GO

IF OBJECT_ID('b3.Figure') IS NULL
CREATE TABLE b3.Figure
(
    Code       NVARCHAR(20)  NOT NULL CONSTRAINT PK_B3Figure PRIMARY KEY,
    Ordinal    NVARCHAR(4)   NULL,
    Name       NVARCHAR(200) NOT NULL,
    /* Whether B3 calculates settlement for the figure. */
    Calculated BIT           NOT NULL CONSTRAINT DF_B3Figure_Calculated DEFAULT 0
);
GO

IF OBJECT_ID('b3.Domain') IS NULL
CREATE TABLE b3.Domain
(
    DomainType     NVARCHAR(80)  NOT NULL,
    Code           NVARCHAR(20)  NOT NULL,
    Name           NVARCHAR(200) NOT NULL,
    Description    NVARCHAR(400) NULL,
    Enabled        BIT           NOT NULL CONSTRAINT DF_B3Domain_Enabled DEFAULT 1,
    InstrumentType NVARCHAR(10)  NULL,
    CONSTRAINT PK_B3Domain PRIMARY KEY (DomainType, Code)
);
GO

IF OBJECT_ID('b3.StrategyField') IS NULL
CREATE TABLE b3.StrategyField
(
    Code      NVARCHAR(20)  NOT NULL CONSTRAINT PK_B3StrategyField PRIMARY KEY,
    Name      NVARCHAR(200) NOT NULL,
    DataType  NVARCHAR(20)  NOT NULL,
    Length    INT           NOT NULL,
    Decimals  INT           NOT NULL,
    Mandatory BIT           NOT NULL
);
GO

IF OBJECT_ID('b3.StrategyFieldValue') IS NULL
CREATE TABLE b3.StrategyFieldValue
(
    FieldCode NVARCHAR(20)  NOT NULL,
    Value     NVARCHAR(200) NOT NULL,
    CONSTRAINT PK_B3StrategyFieldValue PRIMARY KEY (FieldCode, Value),
    CONSTRAINT FK_B3StrategyFieldValue_Field FOREIGN KEY (FieldCode) REFERENCES b3.StrategyField(Code)
);
GO

/* When each export was last loaded, so an operator can see what the platform is checking against. */
IF OBJECT_ID('b3.ReferenceLoad') IS NULL
CREATE TABLE b3.ReferenceLoad
(
    Export     NVARCHAR(60)   NOT NULL CONSTRAINT PK_B3ReferenceLoad PRIMARY KEY,
    AsOf       NVARCHAR(20)   NULL,
    RowCountLoaded INT        NOT NULL,
    LoadedUtc  DATETIMEOFFSET NOT NULL
);
GO
