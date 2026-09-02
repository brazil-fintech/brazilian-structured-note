/* --------------------------------------------------------------------------
   The upload files a registration was sent as.

   A generated file is not a derived value that can be recomputed at will: it
   is what B3 received on a given day, under a given participant name, from
   the values the asset held at that moment. An edit to the asset, a new
   template version or a change of the issuer's short name would all produce
   a different file from the same certificate, so the bytes are kept rather
   than regenerated — that is the whole reason for this table.

   They are kept as VARBINARY, exactly as they would be uploaded: CETIP reads
   a single-byte encoding, and storing text would leave the re-encoding to
   whoever reads the row back. The preview decodes them; the download does not
   touch them.

   Idempotent and additive, like every script here.
   -------------------------------------------------------------------------- */

IF SCHEMA_ID('clearing') IS NULL EXEC('CREATE SCHEMA clearing');
GO

/* One generation: the files a certificate produced in a single pass, together,
   because a Registro COE and the Fluxo de Caixa that completes it are only
   meaningful as the pair that went out. */
IF OBJECT_ID('clearing.FileSet') IS NULL
CREATE TABLE clearing.FileSet
(
    Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ClearingFileSet PRIMARY KEY
                    CONSTRAINT DF_ClearingFileSet_Id DEFAULT NEWSEQUENTIALID(),
    AssetId         UNIQUEIDENTIFIER NOT NULL
                    CONSTRAINT FK_ClearingFileSet_Asset REFERENCES asset.Asset(Id),

    /* What the files were written from and under, so a stored set can be read
       without inferring any of it from the asset as it stands today. */
    FigureCode      NVARCHAR(20)     NOT NULL,
    TemplateVersion INT              NOT NULL,
    ParticipantName NVARCHAR(60)     NOT NULL,
    FileDate        DATE             NOT NULL,

    /* The generator's notes: what went into the files, and what could not. */
    NotesJson       NVARCHAR(MAX)    NULL,

    GeneratedUtc    DATETIMEOFFSET   NOT NULL,
    GeneratedBy     NVARCHAR(100)    NULL,

    CONSTRAINT CK_ClearingFileSet_Notes CHECK (NotesJson IS NULL OR ISJSON(NotesJson) = 1)
);
GO

IF OBJECT_ID('clearing.File') IS NULL
CREATE TABLE clearing.File
(
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ClearingFile PRIMARY KEY
                CONSTRAINT DF_ClearingFile_Id DEFAULT NEWSEQUENTIALID(),
    SetId       UNIQUEIDENTIFIER NOT NULL
                CONSTRAINT FK_ClearingFile_Set REFERENCES clearing.FileSet(Id) ON DELETE CASCADE,

    Layout      NVARCHAR(80)     NOT NULL,   /* "4.8.1 Registro COE" */
    Operation   NVARCHAR(10)     NOT NULL,   /* the code its header carries: 0001, FLUX, … */
    FileName    NVARCHAR(120)    NOT NULL,
    RecordCount INT              NOT NULL,

    /* The upload itself, single-byte encoded. */
    Content     VARBINARY(MAX)   NOT NULL,
    ByteCount   INT              NOT NULL,

    /* sha256:… over Content. Two generations of the same certificate on the
       same day should produce the same file, and this is what says whether
       they did without reading both back. */
    ContentHash NVARCHAR(80)     NOT NULL,

    /* One file per operation within a set: a certificate produces at most one
       Registro COE, one Fluxo de Caixa, one RegistroCestas, one Datas Fixing. */
    CONSTRAINT UQ_ClearingFile_Operation UNIQUE (SetId, Operation)
);
GO

/* The screen lists an asset's generations newest first. */
IF IndexProperty(OBJECT_ID('clearing.FileSet'), 'IX_ClearingFileSet_Asset', 'IndexId') IS NULL
CREATE INDEX IX_ClearingFileSet_Asset ON clearing.FileSet (AssetId, GeneratedUtc DESC);
GO
