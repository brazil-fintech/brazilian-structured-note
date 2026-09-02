/* --------------------------------------------------------------------------
   B3's derivative-data dictionary and its per-figure attribute lists.

   Two more exports from ftp://ftp.cetip.com.br/Public, loaded the same way as
   everything in 003: replaced whole on every pass, never merged.

   DTpTipoDadosDerivativo is the dictionary the Registro COE upload writes
   against — the file the ENVIAR ARQUIVOS layout names for the "Identificador
   do Campo" of its variable-data record — and DTpFigurasDadosDerivativo says
   which of its fields each figure registers. Together they are the published
   answer to "what does this figure hold?", which until now had to be read out
   of the prose annex of the Manual de Operações.

   It is not the same dictionary as b3.StrategyField, and the codes do not
   agree: C0000001 is "Strike 1(%)" here and "% Capital Protegido" there. Both
   are kept, because both are published and each is authoritative for its own
   file.
   -------------------------------------------------------------------------- */

IF OBJECT_ID('b3.DerivativeField') IS NULL
CREATE TABLE b3.DerivativeField
(
    Code      NVARCHAR(20)  NOT NULL CONSTRAINT PK_B3DerivativeField PRIMARY KEY,
    Name      NVARCHAR(300) NOT NULL,
    DataType  NVARCHAR(20)  NOT NULL,
    Length    INT           NOT NULL,
    Decimals  INT           NOT NULL,
    Mandatory BIT           NOT NULL
);
GO

/* One row per value a DOMINIO field accepts. ValueCode is what goes on the wire. */
IF OBJECT_ID('b3.DerivativeFieldValue') IS NULL
CREATE TABLE b3.DerivativeFieldValue
(
    FieldCode NVARCHAR(20)  NOT NULL,
    Name      NVARCHAR(300) NOT NULL,
    ValueCode NVARCHAR(20)  NULL,
    CONSTRAINT PK_B3DerivativeFieldValue PRIMARY KEY (FieldCode, Name),
    CONSTRAINT FK_B3DerivativeFieldValue_Field FOREIGN KEY (FieldCode) REFERENCES b3.DerivativeField(Code)
);
GO

/* Which fields belong to which figure, in the order B3 lists them. Mandatory is
   the figure's own flag, which can be stricter than the dictionary's. */
IF OBJECT_ID('b3.FigureAttribute') IS NULL
CREATE TABLE b3.FigureAttribute
(
    FigureCode NVARCHAR(20) NOT NULL,
    FieldCode  NVARCHAR(20) NOT NULL,
    Position   INT          NOT NULL,
    Mandatory  BIT          NOT NULL,
    CONSTRAINT PK_B3FigureAttribute PRIMARY KEY (FigureCode, FieldCode)
);
GO

/* The booking screen and the registration writer both ask for one figure's
   attributes in order, which this answers without touching the table. */
IF IndexProperty(OBJECT_ID('b3.FigureAttribute'), 'IX_B3FigureAttribute_Figure', 'IndexId') IS NULL
CREATE INDEX IX_B3FigureAttribute_Figure
    ON b3.FigureAttribute (FigureCode, Position) INCLUDE (FieldCode, Mandatory);
GO
