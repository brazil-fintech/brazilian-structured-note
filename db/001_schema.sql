/* --------------------------------------------------------------------------
   COE platform — schema.

   Idempotent: the API and the worker run every script in db/ in name order at
   startup, so a fresh database and an existing one converge on the same shape.
   Scripts are additive; never rewrite one that has shipped, add the next number.
   -------------------------------------------------------------------------- */

IF SCHEMA_ID('figure') IS NULL EXEC('CREATE SCHEMA figure');
GO
IF SCHEMA_ID('asset') IS NULL EXEC('CREATE SCHEMA asset');
GO
IF SCHEMA_ID('ref') IS NULL EXEC('CREATE SCHEMA ref');
GO

/* ---------------------------------------------------------------- figures -- */

IF OBJECT_ID('figure.Figure') IS NULL
CREATE TABLE figure.Figure
(
    Code                  NVARCHAR(20)   NOT NULL CONSTRAINT PK_Figure PRIMARY KEY,
    Name                  NVARCHAR(200)  NOT NULL,
    CommercialName        NVARCHAR(200)  NULL,
    DescriptionPt         NVARCHAR(MAX)  NULL,
    DescriptionEn         NVARCHAR(MAX)  NULL,
    Modalities            NVARCHAR(50)   NOT NULL CONSTRAINT DF_Figure_Modalities DEFAULT '',
    Status                NVARCHAR(20)   NOT NULL CONSTRAINT DF_Figure_Status DEFAULT 'Pending',
    ActiveTemplateVersion INT            NULL,
    SourceFile            NVARCHAR(400)  NULL,
    SourceHash            NVARCHAR(80)   NULL,
    LastError             NVARCHAR(MAX)  NULL,
    FirstSeenUtc          DATETIMEOFFSET NOT NULL,
    UpdatedUtc            DATETIMEOFFSET NOT NULL,
    EnabledUtc            DATETIMEOFFSET NULL
);
GO

IF OBJECT_ID('figure.FigureTemplate') IS NULL
CREATE TABLE figure.FigureTemplate
(
    Id            BIGINT         IDENTITY(1,1) CONSTRAINT PK_FigureTemplate PRIMARY KEY,
    FigureCode    NVARCHAR(20)   NOT NULL CONSTRAINT FK_FigureTemplate_Figure REFERENCES figure.Figure(Code),
    Version       INT            NOT NULL,
    SchemaVersion NVARCHAR(10)   NOT NULL,
    TemplateJson  NVARCHAR(MAX)  NOT NULL,
    SourceHash    NVARCHAR(80)   NOT NULL,
    SourceFile    NVARCHAR(400)  NULL,
    IsActive      BIT            NOT NULL CONSTRAINT DF_FigureTemplate_IsActive DEFAULT 0,
    CreatedUtc    DATETIMEOFFSET NOT NULL,
    CreatedBy     NVARCHAR(100)  NULL,
    CONSTRAINT UQ_FigureTemplate_Version UNIQUE (FigureCode, Version),
    CONSTRAINT CK_FigureTemplate_Json CHECK (ISJSON(TemplateJson) = 1)
);
GO

/* At most one active version per figure — the template the booking screen loads. */
IF IndexProperty(OBJECT_ID('figure.FigureTemplate'), 'UX_FigureTemplate_Active', 'IndexId') IS NULL
CREATE UNIQUE INDEX UX_FigureTemplate_Active
    ON figure.FigureTemplate (FigureCode) WHERE IsActive = 1;
GO

IF OBJECT_ID('figure.IngestionRun') IS NULL
CREATE TABLE figure.IngestionRun
(
    Id                 BIGINT         IDENTITY(1,1) CONSTRAINT PK_IngestionRun PRIMARY KEY,
    StartedUtc         DATETIMEOFFSET NOT NULL,
    CompletedUtc       DATETIMEOFFSET NULL,
    FilesScanned       INT            NOT NULL CONSTRAINT DF_IngestionRun_Files DEFAULT 0,
    FiguresCreated     INT            NOT NULL CONSTRAINT DF_IngestionRun_Created DEFAULT 0,
    TemplatesCreated   INT            NOT NULL CONSTRAINT DF_IngestionRun_Templates DEFAULT 0,
    FiguresQuarantined INT            NOT NULL CONSTRAINT DF_IngestionRun_Quarantined DEFAULT 0,
    Status             NVARCHAR(30)   NOT NULL,
    Details            NVARCHAR(MAX)  NULL
);
GO

/* ----------------------------------------------------------------- assets -- */

IF OBJECT_ID('asset.Asset') IS NULL
CREATE TABLE asset.Asset
(
    Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Asset PRIMARY KEY
                    CONSTRAINT DF_Asset_Id DEFAULT NEWSEQUENTIALID(),
    FigureCode      NVARCHAR(20)     NOT NULL CONSTRAINT FK_Asset_Figure REFERENCES figure.Figure(Code),
    TemplateVersion INT              NOT NULL,

    /* Denormalized from ValuesJson on every save: the columns the asset list filters on. */
    InstrumentCode  NVARCHAR(20)     NULL,
    IsinCode        NVARCHAR(12)     NULL,
    CommercialName  NVARCHAR(200)    NOT NULL,
    IssuerAccount   NVARCHAR(20)     NULL,
    IssueDate       DATE             NOT NULL,
    MaturityDate    DATE             NOT NULL,
    Modality        NVARCHAR(10)     NULL,
    UnderlyingClass NVARCHAR(30)     NULL,
    Underlying      NVARCHAR(60)     NULL,
    Quantity        BIGINT           NULL,
    UnitIssuePrice  DECIMAL(28,8)    NULL,
    NotionalAmount  DECIMAL(28,8)    NULL,
    Status          NVARCHAR(20)     NOT NULL CONSTRAINT DF_Asset_Status DEFAULT 'Draft',

    ValuesJson      NVARCHAR(MAX)    NOT NULL,
    WarningsJson    NVARCHAR(MAX)    NULL,

    CreatedUtc      DATETIMEOFFSET   NOT NULL,
    CreatedBy       NVARCHAR(100)    NULL,
    UpdatedUtc      DATETIMEOFFSET   NOT NULL,
    UpdatedBy       NVARCHAR(100)    NULL,
    RowVersion      ROWVERSION       NOT NULL,

    CONSTRAINT CK_Asset_Values CHECK (ISJSON(ValuesJson) = 1),
    CONSTRAINT CK_Asset_Tenor CHECK (MaturityDate > IssueDate)
);
GO

/* The asset list filters on "live on the reference date", i.e. IssueDate <= @d <= MaturityDate. */
IF IndexProperty(OBJECT_ID('asset.Asset'), 'IX_Asset_Live', 'IndexId') IS NULL
CREATE INDEX IX_Asset_Live ON asset.Asset (MaturityDate, IssueDate)
    INCLUDE (FigureCode, CommercialName, Modality, Underlying, Status, NotionalAmount);
GO

IF IndexProperty(OBJECT_ID('asset.Asset'), 'IX_Asset_Figure', 'IndexId') IS NULL
CREATE INDEX IX_Asset_Figure ON asset.Asset (FigureCode, MaturityDate);
GO

IF IndexProperty(OBJECT_ID('asset.Asset'), 'UX_Asset_InstrumentCode', 'IndexId') IS NULL
CREATE UNIQUE INDEX UX_Asset_InstrumentCode ON asset.Asset (InstrumentCode)
    WHERE InstrumentCode IS NOT NULL;
GO

/* ------------------------------------------------------------ reference -- */

IF OBJECT_ID('ref.Holiday') IS NULL
CREATE TABLE ref.Holiday
(
    CalendarCode NVARCHAR(20)  NOT NULL,
    HolidayDate  DATE          NOT NULL,
    Description  NVARCHAR(120) NULL,
    CONSTRAINT PK_Holiday PRIMARY KEY (CalendarCode, HolidayDate)
);
GO

IF OBJECT_ID('ref.Underlying') IS NULL
CREATE TABLE ref.Underlying
(
    Code       NVARCHAR(30)  NOT NULL CONSTRAINT PK_Underlying PRIMARY KEY,
    Name       NVARCHAR(120) NOT NULL,
    AssetClass NVARCHAR(30)  NOT NULL,
    IsActive   BIT           NOT NULL CONSTRAINT DF_Underlying_IsActive DEFAULT 1
);
GO
