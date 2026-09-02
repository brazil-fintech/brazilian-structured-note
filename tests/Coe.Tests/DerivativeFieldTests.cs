using Coe.Ingestion;
using Xunit;

namespace Coe.Tests;

/// <summary>
/// B3's derivative-data dictionary and its per-figure attribute lists, read from the exports
/// committed under <c>reference/b3/</c>. These are the files the registration writes against, so
/// what they say is what a booked asset can carry.
/// </summary>
public sealed class DerivativeFieldTests
{
    private static B3Reference Reference => DomainFiles.Reference;
    private static B3DerivativeFields Fields => Reference.DerivativeFields;

    [Fact]
    public void The_exports_load_without_complaint()
    {
        Assert.Empty(Fields.Errors);
        Assert.True(Fields.Fields.Count > 400, $"only {Fields.Fields.Count} field(s) loaded");
    }

    [Fact]
    public void Every_figure_in_the_catalogue_has_its_attributes()
    {
        // Including the four Retorno Condicional figures the manual's annex has no entry for.
        Assert.Equal(Reference.Figures.Count, Fields.FigureCodes.Count);
        foreach (var figure in Reference.Figures)
            Assert.NotEmpty(Fields.ForFigure(figure.Code));
    }

    [Fact]
    public void A_field_carries_the_type_precision_and_mandatory_flag_B3_publishes()
    {
        var strike = Fields.Field("C0000001");

        Assert.NotNull(strike);
        Assert.Equal("Strike 1(%)", strike.Name);
        Assert.Equal("NUMERICO", strike.DataType);
        Assert.Equal(4, strike.Length);
        Assert.Equal(8, strike.Decimals);
        Assert.True(strike.Mandatory);
    }

    [Fact]
    public void A_domain_field_carries_the_identifier_of_every_value_it_takes()
    {
        var capture = Fields.Field("C0000003");

        Assert.NotNull(capture);
        Assert.True(capture.IsDomain);
        Assert.Equal(3, capture.DomainValues.Count);
        Assert.Equal("1", capture.DomainValues.Single(v => v.Name == "Data única").Code);
        Assert.Equal("2", capture.DomainValues.Single(v => v.Name == "Janela de datas").Code);
        Assert.Equal("52", capture.DomainValues.Single(v => v.Name == "MAIS DATAS").Code);
    }

    [Fact]
    public void It_is_a_different_dictionary_from_the_strategy_one()
    {
        // The same code means different attributes in the two exports, which is the reason both
        // are kept and addressed through separate properties.
        Assert.Equal("Strike 1(%)", Fields.Field("C0000001")!.Name);
        Assert.Equal("% Capital Protegido", Reference.StrategyField("C0000001")!.Name);
    }

    [Fact]
    public void A_figures_attributes_can_be_found_by_B3s_own_name_for_them()
    {
        var byName = Reference.FigureAttributesByName("COE001005");

        Assert.NotEmpty(byName);
        Assert.Equal("C0000032",
            byName[B3DerivativeFields.NormalizeName("Limitador cenário de alta (%)")].FieldCode);
        Assert.Equal("C0000007",
            byName[B3DerivativeFields.NormalizeName("Tipo de Cotação para Liquidação")].FieldCode);
    }

    [Theory]
    [InlineData("Participação cenário de alta (%)", "participacao cenario de alta")]
    [InlineData("Participacao Cenario de Alta", "participacao cenario de alta")]
    [InlineData("Strike 1(%)", "strike 1")]
    [InlineData("Strike 1 (%)", "strike 1")]
    [InlineData(null, "")]
    public void Names_reduce_to_what_their_spellings_have_in_common(string? name, string expected) =>
        Assert.Equal(expected, B3DerivativeFields.NormalizeName(name));

    [Fact]
    public void A_catalogue_row_with_no_code_keeps_its_attributes()
    {
        // COE001085 and its TRS twin are published with a name and no code, in both exports; the
        // sequence number is what ties the two rows together.
        Assert.NotEmpty(Fields.ForFigure("COE001085"));
        Assert.NotEmpty(Fields.ForFigure("COE001086"));
    }

    [Fact]
    public void Every_published_attribute_names_a_field_the_dictionary_defines()
    {
        foreach (var figure in Fields.FigureCodes)
            foreach (var attribute in Fields.ForFigure(figure))
                Assert.True(Fields.Field(attribute.FieldCode) is not null,
                    $"{figure} registers {attribute.FieldCode}, which the dictionary does not define");
    }

    [Fact]
    public void Every_attribute_B3_registers_can_be_written_out()
    {
        // The whole point of holding B3's per-figure attribute lists: an attribute the platform
        // cannot address is one the registration file goes out without. This is the assertion
        // that keeps that at zero. If B3 publishes an attribute a figure has no field for, this
        // fails and names it, which is the signal to add the field or the series.
        var unmapped = new List<string>();

        foreach (var figure in DomainFiles.Reference.Figures.OrderBy(f => f.Code, StringComparer.Ordinal))
        {
            var published = DomainFiles.Reference.FigureAttributes(figure.Code);
            if (published.Count == 0) continue;

            var result = DomainFiles.Compiled[figure.Code];
            Assert.True(result.Succeeded, $"{figure.Code} failed to compile: {string.Join("; ", result.Errors)}");

            var addressable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in result.Template!.AllFields())
            {
                if (field.B3DataCode is { } code) addressable.Add(code);
                foreach (var series in field.B3SeriesCodes) addressable.Add(series);
            }

            unmapped.AddRange(published
                .Where(a => !addressable.Contains(a.FieldCode))
                .Select(a => $"{figure.Code} {a.FieldCode} '{a.Name}'"));
        }

        Assert.True(unmapped.Count == 0,
            $"{unmapped.Count} published attribute(s) have no field:{Environment.NewLine}"
            + string.Join(Environment.NewLine, unmapped));
    }

    [Fact]
    public void A_repeating_column_maps_to_the_numbered_run_B3_registers()
    {
        // B3's file format is flat, so a schedule the form shows as rows is a run of numbered
        // fields to it. The observation dates of the autocall figure are ten of them.
        var dates = DomainFiles.Template("COE001064").FindField("observations[].observationDate");

        Assert.NotNull(dates);
        Assert.Equal(10, dates.B3SeriesCodes.Count);
        Assert.Equal("C0000128", dates.B3SeriesCodes[0]);
        Assert.Equal("C0000137", dates.B3SeriesCodes[9]);

        // And B3 pairs each date with the participation used on it.
        var participation = DomainFiles.Template("COE001064").FindField("observations[].participation");
        Assert.NotNull(participation);
        Assert.Equal(10, participation.B3SeriesCodes.Count);
        Assert.Equal("C0000109", participation.B3SeriesCodes[0]);
    }

    [Fact]
    public void A_figure_with_no_such_series_gets_an_empty_one()
    {
        // The series is declared on the shared cash-flow block, so every figure that extends it
        // asks the question; only the ones B3 registers an amortisation run for get an answer.
        Assert.Equal(12, DomainFiles.Template("COE001072").FindField("cashflows[].amountPercent")!.B3SeriesCodes.Count);
        Assert.Empty(DomainFiles.Template("COE001005").FindField("cashflows[].amountPercent")!.B3SeriesCodes);
    }

    [Theory]
    [InlineData("Strike 1(%)", "Strike 2(%)")]
    [InlineData("Barreira cenário de alta (%)", "Barreira cenário de baixa (%)")]
    [InlineData("Data de Observação 1", "Data de Observação 10")]
    public void Attributes_that_differ_in_substance_stay_apart(string one, string other) =>
        Assert.NotEqual(B3DerivativeFields.SignatureOf(one), B3DerivativeFields.SignatureOf(other));

    [Theory]
    [InlineData("Data de verificação amortização 1", "Data verificação amortização 1")]
    [InlineData("Barreira no Cenário de Alta", "Barreira cenário de alta (%)")]
    [InlineData("Tipo de Cotação para Verificação de Barreiras no Cenário de Alta",
                "Tipo de cotação para verificação de barreira cenario de alta")]
    public void The_same_attribute_spelled_two_ways_reduces_alike(string annex, string export) =>
        Assert.Equal(B3DerivativeFields.SignatureOf(annex), B3DerivativeFields.SignatureOf(export));

    [Fact]
    public void No_figure_has_two_attributes_that_reduce_alike()
    {
        // What makes signature matching safe to fall back on: within a figure the reduced names
        // are still distinct, so a match is never a choice between two attributes.
        foreach (var figure in DomainFiles.Reference.Figures)
        {
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var attribute in DomainFiles.Reference.FigureAttributes(figure.Code))
            {
                var signature = B3DerivativeFields.SignatureOf(attribute.Name);
                Assert.False(seen.TryGetValue(signature, out var first),
                    $"{figure.Code}: '{attribute.Name}' and '{first}' reduce to the same words");
                seen[signature] = attribute.Name;
            }
        }
    }
}
