using System.Data;
using System.Diagnostics;
using Coe.Core.Diagnostics;
using Coe.Infrastructure.Data;
using Coe.Ingestion;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Coe.Infrastructure;

public sealed record ReferenceImportReport(
    int Figures, int DomainValues, int StrategyFields, int Underlyings,
    int DerivativeFields, int FigureAttributes, bool Skipped);

/// <summary>
/// Loads B3's published exports into the <c>b3.*</c> tables and the underlying master.
///
/// The export is the whole truth for these tables, so each one is replaced rather than merged:
/// a figure or an underlying that disappears from a newer export must disappear here too, and a
/// merge would silently keep it. The replacement runs in one transaction, so a reader never sees
/// a half-loaded catalogue.
/// </summary>
public sealed class B3ReferenceImporter(
    ISqlConnectionFactory connections,
    B3ReferenceProvider references,
    ILogger<B3ReferenceImporter> logger)
{
    /// <summary>Rows per network round trip. The largest export here is ~7,800 rows.</summary>
    private const int BatchSize = 2_000;

    public async Task<ReferenceImportReport> ImportAsync(CancellationToken ct = default)
    {
        // One snapshot for the whole import: a set swapped in mid-transaction would put half of
        // one export and half of another into the same replacement.
        var reference = references.Current;

        if (reference.Figures.Count == 0 && reference.Underlyings.Count == 0)
        {
            logger.LogWarning("No B3 reference export was loaded; leaving the reference tables untouched");
            return new ReferenceImportReport(0, 0, 0, 0, 0, 0, Skipped: true);
        }

        var started = Stopwatch.GetTimestamp();
        using var activity = CoeDiagnostics.Ingestion.StartActivity("coe.reference.import", ActivityKind.Internal);

        await using var connection = await connections.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // The value tables reference their field tables, so they clear first.
        await ExecuteAsync(connection, transaction, """
            DELETE FROM b3.StrategyFieldValue;
            DELETE FROM b3.StrategyField;
            DELETE FROM b3.DerivativeFieldValue;
            DELETE FROM b3.FigureAttribute;
            DELETE FROM b3.DerivativeField;
            DELETE FROM b3.Domain;
            DELETE FROM b3.Figure;
            DELETE FROM ref.Underlying;
            """, ct);

        var figures = await BulkCopyAsync(connection, transaction, "b3.Figure", FigureTable(reference), ct);
        var domains = await BulkCopyAsync(connection, transaction, "b3.Domain", DomainTable(reference), ct);
        var fields = await BulkCopyAsync(connection, transaction, "b3.StrategyField", StrategyFieldTable(reference), ct);
        await BulkCopyAsync(connection, transaction, "b3.StrategyFieldValue", StrategyFieldValueTable(reference), ct);
        var underlyings = await BulkCopyAsync(connection, transaction, "ref.Underlying", UnderlyingTable(reference), ct);
        var dataFields = await BulkCopyAsync(connection, transaction, "b3.DerivativeField", DerivativeFieldTable(reference), ct);
        await BulkCopyAsync(connection, transaction, "b3.DerivativeFieldValue", DerivativeFieldValueTable(reference), ct);
        var attributes = await BulkCopyAsync(connection, transaction, "b3.FigureAttribute", FigureAttributeTable(reference), ct);

        var derivativeAsOf = reference.DerivativeFields.AsOf;
        await RecordLoadAsync(connection, transaction, "figuras", reference.AsOf, figures, ct);
        await RecordLoadAsync(connection, transaction, "dominios", null, domains, ct);
        await RecordLoadAsync(connection, transaction, "dados-estrategia", null, fields, ct);
        await RecordLoadAsync(connection, transaction, "ativos-subjacentes", null, underlyings, ct);
        await RecordLoadAsync(connection, transaction, "dados-derivativo", derivativeAsOf, dataFields, ct);
        await RecordLoadAsync(connection, transaction, "figuras-dados-derivativo", derivativeAsOf, attributes, ct);

        await transaction.CommitAsync(ct);

        var elapsed = Stopwatch.GetElapsedTime(started);
        logger.LogInformation(
            "Loaded B3 reference data in {Elapsed}: {Figures} figure(s), {Domains} domain value(s), " +
            "{Fields} strategy field(s), {Underlyings} underlying(s), {DataFields} derivative field(s), " +
            "{Attributes} figure attribute(s)",
            elapsed, figures, domains, fields, underlyings, dataFields, attributes);

        activity?.SetTag("coe.reference.figures", figures);
        activity?.SetTag("coe.reference.underlyings", underlyings);
        activity?.SetTag("coe.reference.figure_attributes", attributes);

        return new ReferenceImportReport(figures, domains, fields, underlyings, dataFields, attributes, Skipped: false);
    }

    // ----- table projections ----------------------------------------------------------

    private static DataTable FigureTable(B3Reference reference)
    {
        var table = NewTable(("Code", typeof(string)), ("Ordinal", typeof(string)),
            ("Name", typeof(string)), ("Calculated", typeof(bool)));
        foreach (var f in reference.Figures)
            table.Rows.Add(f.Code, f.Ordinal, f.Name, f.Calculated);
        return table;
    }

    private static DataTable DomainTable(B3Reference reference)
    {
        var table = NewTable(("DomainType", typeof(string)), ("Code", typeof(string)),
            ("Name", typeof(string)), ("Description", typeof(string)),
            ("Enabled", typeof(bool)), ("InstrumentType", typeof(string)));

        // A domain type can list the same code for different instrument types; the table is
        // keyed on (type, code), so the first one wins and the rest are duplicates.
        var seen = new HashSet<(string, string)>();
        foreach (var values in reference.Domains.Values)
            foreach (var v in values)
                if (seen.Add((v.DomainType, v.Code)))
                    table.Rows.Add(v.DomainType, v.Code, v.Name,
                        (object?)v.Description ?? DBNull.Value, v.Enabled,
                        (object?)v.InstrumentType ?? DBNull.Value);
        return table;
    }

    private static DataTable StrategyFieldTable(B3Reference reference)
    {
        var table = NewTable(("Code", typeof(string)), ("Name", typeof(string)),
            ("DataType", typeof(string)), ("Length", typeof(int)),
            ("Decimals", typeof(int)), ("Mandatory", typeof(bool)));
        foreach (var f in reference.StrategyFields.Values)
            table.Rows.Add(f.Code, f.Name, f.DataType, f.Length, f.Decimals, f.Mandatory);
        return table;
    }

    private static DataTable StrategyFieldValueTable(B3Reference reference)
    {
        var table = NewTable(("FieldCode", typeof(string)), ("Value", typeof(string)));
        foreach (var f in reference.StrategyFields.Values)
            foreach (var value in f.DomainValues.Distinct(StringComparer.Ordinal))
                table.Rows.Add(f.Code, value);
        return table;
    }

    private static DataTable UnderlyingTable(B3Reference reference)
    {
        var table = NewTable(("AssetClass", typeof(string)), ("Code", typeof(string)),
            ("ValuationIndex", typeof(string)), ("Exchange", typeof(string)),
            ("Currency", typeof(string)), ("Ticker", typeof(string)),
            ("Calculated", typeof(bool)), ("IsActive", typeof(bool)));

        var seen = new HashSet<(string, string, string)>();
        foreach (var u in reference.Underlyings)
        {
            // Only COE-eligible rows: the same master serves options, swaps and forwards.
            if (u.InstrumentType != B3Reference.CoeInstrumentType) continue;
            if (string.IsNullOrWhiteSpace(u.Code)) continue;

            var valuationIndex = string.IsNullOrWhiteSpace(u.ValuationIndex) ? u.Code : u.ValuationIndex;
            if (!seen.Add((u.AssetClass, u.Code, valuationIndex))) continue;

            table.Rows.Add(u.AssetClass, u.Code, valuationIndex,
                (object?)NullIfBlank(u.Exchange) ?? DBNull.Value,
                (object?)NullIfBlank(u.Currency) ?? DBNull.Value,
                (object?)NullIfBlank(u.Ticker) ?? DBNull.Value,
                u.Calculated, true);
        }
        return table;
    }

    private static DataTable DerivativeFieldTable(B3Reference reference)
    {
        var table = NewTable(("Code", typeof(string)), ("Name", typeof(string)),
            ("DataType", typeof(string)), ("Length", typeof(int)),
            ("Decimals", typeof(int)), ("Mandatory", typeof(bool)));
        foreach (var f in reference.DerivativeFields.Fields.Values)
            table.Rows.Add(f.Code, f.Name, f.DataType, f.Length, f.Decimals, f.Mandatory);
        return table;
    }

    private static DataTable DerivativeFieldValueTable(B3Reference reference)
    {
        var table = NewTable(("FieldCode", typeof(string)), ("Name", typeof(string)), ("ValueCode", typeof(string)));

        foreach (var f in reference.DerivativeFields.Fields.Values)
        {
            // B3 repeats a value name within a field where it carries two identifiers; the
            // table is keyed on (field, name), so the first one wins.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in f.DomainValues)
                if (seen.Add(value.Name))
                    table.Rows.Add(f.Code, value.Name, (object?)value.Code ?? DBNull.Value);
        }
        return table;
    }

    private static DataTable FigureAttributeTable(B3Reference reference)
    {
        var table = NewTable(("FigureCode", typeof(string)), ("FieldCode", typeof(string)),
            ("Position", typeof(int)), ("Mandatory", typeof(bool)));

        foreach (var figureCode in reference.DerivativeFields.FigureCodes)
            foreach (var attribute in reference.DerivativeFields.ForFigure(figureCode))
                table.Rows.Add(figureCode, attribute.FieldCode, attribute.Position, attribute.Mandatory);

        return table;
    }

    // ----- plumbing --------------------------------------------------------------------

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static DataTable NewTable(params (string Name, Type Type)[] columns)
    {
        var table = new DataTable();
        foreach (var (name, type) in columns) table.Columns.Add(name, type);
        return table;
    }

    private static async Task<int> BulkCopyAsync(
        SqlConnection connection, SqlTransaction transaction, string destination, DataTable table, CancellationToken ct)
    {
        if (table.Rows.Count == 0) return 0;

        using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
        {
            DestinationTableName = destination,
            BatchSize = BatchSize,
            BulkCopyTimeout = 120
        };

        // Map by name: the projections above do not list columns in table order.
        foreach (DataColumn column in table.Columns)
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);

        await bulk.WriteToServerAsync(table, ct);
        return table.Rows.Count;
    }

    private static async Task ExecuteAsync(
        SqlConnection connection, SqlTransaction transaction, string sql, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task RecordLoadAsync(
        SqlConnection connection, SqlTransaction transaction, string export, string? asOf, int rows, CancellationToken ct)
    {
        const string sql = """
            UPDATE b3.ReferenceLoad
               SET AsOf = @asOf, RowCountLoaded = @rows, LoadedUtc = @loadedUtc
             WHERE Export = @export;

            IF @@ROWCOUNT = 0
                INSERT INTO b3.ReferenceLoad (Export, AsOf, RowCountLoaded, LoadedUtc)
                VALUES (@export, @asOf, @rows, @loadedUtc);
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.NVarChar("@export", export, 60);
        command.NVarChar("@asOf", asOf, 20);
        command.Int("@rows", rows);
        command.DateTimeOffset("@loadedUtc", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(ct);
    }
}
