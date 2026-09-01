using Coe.DomainGen;
using Coe.Ingestion;
using Xunit;

namespace Coe.Tests;

/// <summary>
/// The figure-attribute annex of B3's Manual de Operações, and the reading of it that turns a
/// printed table into attributes. It is extracted from a PDF by hand and committed, so these
/// guard the extraction against a re-run that silently loses rows.
/// </summary>
public class AnnexTests
{
    private static B3FigureFields Annex => DomainFiles.Reference.FigureFields;

    [Fact]
    public void The_annex_loads_without_errors() => Assert.Empty(Annex.Errors);

    [Fact]
    public void It_covers_every_figure_whose_annex_B3_still_publishes()
    {
        // B3 withdrew the Dados Específicos of the Retorno Condicional family in September 2024
        // (change log, page 5 of the manual). Those four figures have no attribute list anywhere.
        string[] withdrawn = ["COE001053", "COE001057", "COE001072", "COE001076"];

        var missing = DomainFiles.Reference.Figures
            .Select(f => f.Code)
            .Where(code => Annex.Fields(code).Count == 0)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(withdrawn, missing);
    }

    [Fact]
    public void A_figures_attributes_come_through_intact()
    {
        var call = Annex.Fields("COE001001");

        Assert.Contains(call, f => f.Label == "Strike 1 (%)");
        Assert.Contains(call, f => f.Label == "Participação cenário de alta (%)");
        Assert.Contains(call, f => f.Label == "Período de captura do ativo subjacente para liquidação");

        var strike = call.First(f => f.Label == "Strike 1 (%)");
        Assert.Contains("obrigatório", strike.Description);
        Assert.Contains("4 inteiros e 8 decimais", strike.Description);
    }

    [Fact]
    public void The_annex_supplies_the_codes_the_export_omits() =>
        Assert.Equal("COE001085", Annex.CodeForName("COE de Crédito – CDS com Amortização"));

    [Theory]
    // The same attribute is printed several ways across the annex; they must land on one key.
    [InlineData("Strike 1 (%)", "strike1")]
    [InlineData("Strike 1(%)", "strike1")]
    [InlineData("STRIKE 1 %", "strike1")]
    [InlineData("Participação cenário de alta (%)", "upsideParticipation")]
    [InlineData("Participação cenário de Alta (%)", "upsideParticipation")]
    [InlineData("Participação cenário de baixa 2 (%)", "downsideParticipation2")]
    [InlineData("Limitador Cenário de Alta (%)", "upsideCap")]
    [InlineData("Rebate KI (%)", "kiRebate")]
    [InlineData("Data de Observação 12", "observationDate12")]
    public void Printed_variants_of_one_attribute_share_a_key(string label, string expected) =>
        Assert.Equal(expected, Vocabulary.Place(Vocabulary.Normalize(label)).Key);

    [Fact]
    public void An_attribute_the_common_blocks_already_carry_is_inherited() =>
        Assert.Equal(
            "underlying.fixingWindow",
            Vocabulary.CoveredBy(Vocabulary.Normalize("Período de captura do ativo subjacente para liquidação")));

    [Fact]
    public void A_percentage_field_takes_its_type_precision_and_bound_from_B3s_format()
    {
        var draft = FieldInterpreter.Interpret(new B3FigureField(
            "COE001001", 1, "Strike 1 (%)",
            "Campo de preenchimento obrigatório. Formato: Numérico percentual com 4 inteiros e "
            + "8 decimais, maior que 0. Percentual aplicado sobre o Valor Inicial do Ativo Subjacente."), 10);

        Assert.Equal("percent", draft.Field.DataType);
        Assert.Equal(8, draft.Field.Decimals);
        Assert.Equal(9999m, draft.Field.Max);
        Assert.Equal(0m, draft.Field.Min);
        Assert.True(draft.Field.Required);
        Assert.True(draft.PositiveOnly);
    }

    [Fact]
    public void A_listed_domain_becomes_the_fields_options()
    {
        var draft = FieldInterpreter.Interpret(new B3FigureField(
            "COE001001", 1, "Período de captura do ativo subjacente para liquidação",
            "Campo de preenchimento obrigatório. Campo com as opções: Data Única, Janela de Datas e Mais Datas."), 10);

        Assert.Equal("enum", draft.Field.DataType);
        Assert.Equal(["DATA_UNICA", "JANELA_DE_DATAS", "MAIS_DATAS"], draft.Field.Options.Select(o => o.Code));
    }

    [Fact]
    public void A_conditional_requirement_keeps_the_value_it_hangs_on()
    {
        var draft = FieldInterpreter.Interpret(new B3FigureField(
            "COE001001", 1, "Data inicial para fixing",
            "Campo de preenchimento obrigatório, se indicado “Janela de datas”. Formato: DD/MM/AAAA. "
            + "Não preencher se a “Classe do Ativo Subjacente” for igual a “CESTA”."), 10);

        Assert.Equal("date", draft.Field.DataType);
        Assert.False(draft.Field.Required);
        Assert.Equal("Janela de datas", draft.RequiredWhenValue);
        Assert.Equal("underlying.assetClass != 'CESTA'", draft.Field.VisibleWhen);
    }

    [Fact]
    public void Every_figure_B3_documents_can_be_booked()
    {
        // The point of generating: a figure whose attributes B3 publishes has a form here, so
        // the picker offers the catalogue rather than the handful somebody wrote out.
        var modelled = DomainFiles.Set.Figures
            .Select(f => f.File.FigureCode!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var documented = DomainFiles.Reference.Figures
            .Select(f => f.Code)
            .Where(code => Annex.Fields(code).Count > 0)
            .ToArray();

        Assert.Equal(84, documented.Length);
        Assert.All(documented, code => Assert.Contains(code, modelled));
    }

    [Fact]
    public void Every_figure_in_B3s_catalogue_has_a_form()
    {
        // Including the four the annex does not cover. A figure with no attributes of its own is
        // not an unbookable figure — the Retorno Condicional family redeems principal plus
        // interest, so it is booked entirely from the common registration blocks.
        var modelled = DomainFiles.Set.Figures
            .Select(f => f.File.FigureCode!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = DomainFiles.Reference.Figures
            .Select(f => f.Code)
            .Where(code => !modelled.Contains(code))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missing);
        Assert.Equal(88, DomainFiles.Reference.Figures.Count);
    }

    [Fact]
    public void A_generated_figure_is_shadowed_by_a_curated_one()
    {
        // COE001005 has a hand-written file; the loader must not also serve a generated twin.
        var files = DomainFiles.Set.Figures
            .Where(f => string.Equals(f.File.FigureCode, "COE001005", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var loaded = Assert.Single(files);
        Assert.DoesNotContain(DomainFileLoader.GeneratedFolder, loaded.RelativePath, StringComparison.Ordinal);
    }

    [Fact]
    public void A_note_is_not_mistaken_for_an_attribute() =>
        Assert.False(FieldInterpreter.IsField(new B3FigureField(
            "COE001050", 1, "Campos Fixos – Dados do Lookback", string.Empty)));
}
