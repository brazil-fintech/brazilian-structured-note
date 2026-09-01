namespace Coe.Clearing;

/// <summary>
/// The files one booked certificate produces, with a line per file saying why it is there — and,
/// where a file is missing, why it is not.
/// </summary>
public sealed record ClearingFileSet(IReadOnlyList<CetipFile> Files, IReadOnlyList<string> Notes)
{
    public CetipFile? Find(string operation) =>
        Files.FirstOrDefault(f => string.Equals(f.Operation, operation, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Decides which of the registration-time upload files a certificate needs.
///
/// The registration is always one of them. The other three exist because B3 accepts a
/// registration that is deliberately incomplete and then waits: a cash-flow certificate is
/// registered PENDENTE FLUXO DE CAIXA, a basket needs its components, and a "Mais Datas" capture
/// period needs its schedule. Each is sent only when the booked values call for it, which is the
/// same condition the booking screen shows the tab under.
/// </summary>
public static class ClearingFileGenerator
{
    public static ClearingFileSet ForRegistration(ClearingRequest request)
    {
        var reader = request.Reader();
        var files = new List<CetipFile> { CetipRegistrationFiles.Registration(request) };
        var notes = new List<string>();

        var variables = reader.VariableFields().Count;
        notes.Add($"Registro COE: {variables} attribute(s) in the variable-data record.");

        if (string.IsNullOrWhiteSpace(request.FigureOrdinal))
            notes.Add("No figure sequence was given, so 'Tipo COE' went out as 00; B3 publishes it in DTpFiguras.");

        if (reader.Flag("remuneration.hasCashFlow"))
        {
            var cashFlow = CetipRegistrationFiles.CashFlow(request);
            files.Add(cashFlow);
            notes.Add($"Fluxo de Caixa: {cashFlow.RecordCount} event(s).");
        }

        if (string.Equals(reader.Text("underlying.assetClass"), "CESTA", StringComparison.OrdinalIgnoreCase))
        {
            var basket = CetipRegistrationFiles.Basket(request);
            files.Add(basket);
            notes.Add($"RegistroCestas: {basket.RecordCount} component(s).");
        }

        if (string.Equals(reader.Text("underlying.fixingWindow"), "MAIS_DATAS", StringComparison.Ordinal))
        {
            var fixings = CetipRegistrationFiles.FixingDates(request);
            files.Add(fixings);
            notes.Add($"Datas Fixing: {fixings.RecordCount} date(s).");
        }

        // An attribute B3 publishes for the figure but the domain file cannot address is one the
        // registration goes out without. Saying so beside the file is the only place a desk
        // would notice before B3 does.
        var unmapped = reader.Template.AllFields().Count(f => f.B3DataCode is null && !IsRegistrationField(f.Path));
        if (unmapped > 0)
            notes.Add($"{unmapped} attribute(s) carry no B3 data code and are not in the variable-data record.");

        return new ClearingFileSet(files, notes);
    }

    /// <summary>
    /// Whether a path belongs to the fixed-data record rather than the variable one. Those
    /// attributes have positions of their own in the layout and no data code by design, so they
    /// are not part of the "unmapped" count.
    /// </summary>
    private static bool IsRegistrationField(string path) =>
        path.StartsWith("common.", StringComparison.Ordinal) ||
        path.StartsWith("terms.", StringComparison.Ordinal) ||
        path.StartsWith("deposit.", StringComparison.Ordinal) ||
        path.StartsWith("basket[]", StringComparison.Ordinal) ||
        path.StartsWith("cashflows[]", StringComparison.Ordinal) ||
        path.StartsWith("fixingDates[]", StringComparison.Ordinal);
}
