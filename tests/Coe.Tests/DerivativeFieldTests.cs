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
    public void Most_of_what_B3_registers_can_be_written_out()
    {
        // The compiler matches a field to a published attribute by B3's own name for it. That
        // does not reach everything — B3 names an attribute one way in the export and another on
        // the registration screen often enough — so this is a floor, not a claim of completeness,
        // and it exists to catch a change that quietly stops the matching from working.
        var resolved = DomainFiles.Compiled.Values
            .Where(result => result.Succeeded)
            .SelectMany(result => result.Template!.AllFields())
            .Count(field => field.B3DataCode is not null);

        Assert.True(resolved > 900, $"only {resolved} attribute(s) resolved to a B3 data code");
    }

    [Fact]
    public void A_figure_whose_attributes_none_of_the_files_name_is_reported_not_hidden()
    {
        // Four figures match nothing today: two credit COEs B3 publishes without a code, the
        // fund-amortisation figure, and the range accrual, whose curated file uses its own names.
        // The compiler says so per figure rather than leaving a silent gap.
        var silent = DomainFiles.Compiled
            .Where(pair => pair.Value.Succeeded)
            .Where(pair => pair.Value.Template!.AllFields().All(f => f.B3DataCode is null))
            .Where(pair => DomainFiles.Reference.FigureAttributes(pair.Key).Count > 0)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var code in silent)
        {
            Assert.Contains(
                DomainFiles.Compiled[code].Warnings,
                warning => warning.Contains("carries no b3DataCode", StringComparison.Ordinal));
        }
    }
}
