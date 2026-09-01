using Coe.Ingestion;
using Xunit;

namespace Coe.Tests;

/// <summary>
/// The domain files describe what a desk may book; B3's exports say what B3 will accept. These
/// keep the two in step, so a figure B3 renames or an option code it retires fails ingestion
/// rather than a registration.
/// </summary>
public class B3ReferenceTests
{
    private static B3Reference Reference => DomainFiles.Reference;

    [Fact]
    public void The_reference_exports_load_without_errors() =>
        Assert.Empty(Reference.Errors);

    [Fact]
    public void The_figure_catalogue_covers_the_published_range()
    {
        // B3 publishes COE001001–COE001088. Two rows of the export carry a name with no code;
        // their codes come from the manual's annex, so the catalogue is complete.
        Assert.Equal(88, Reference.Figures.Count);
        Assert.NotNull(Reference.Figure("COE001085"));
        Assert.NotNull(Reference.Figure("COE001086"));
        Assert.NotNull(Reference.Figure("COE001005"));
        Assert.Equal("Call Spread", Reference.Figure("COE001005")!.Name);
        Assert.True(Reference.Figure("COE001005")!.Calculated);
        Assert.False(Reference.Figure("COE001073")!.Calculated);
    }

    [Fact]
    public void No_figure_advertises_itself_under_another_figures_registered_name()
    {
        // A commercial name is a house label, but one that happens to be B3's registered name for
        // a different figure heads the booking screen with the wrong identity: COE001001 once
        // called itself "Call com participação", which is what B3 registers COE001064 as.
        var registered = Reference.Figures.ToDictionary(f => f.Name, f => f.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var loaded in DomainFiles.Set.Figures)
        {
            var commercial = loaded.File.CommercialName;
            if (string.IsNullOrWhiteSpace(commercial)) continue;
            if (!registered.TryGetValue(commercial, out var owner)) continue;

            Assert.True(
                string.Equals(owner, loaded.File.FigureCode, StringComparison.OrdinalIgnoreCase),
                $"{loaded.RelativePath}: commercialName '{commercial}' is B3's name for {owner}.");
        }
    }

    [Fact]
    public void Every_booked_figure_exists_in_the_catalogue_under_the_name_B3_gives_it()
    {
        foreach (var loaded in DomainFiles.Set.Figures)
        {
            var code = loaded.File.FigureCode!;
            var published = Reference.Figure(code);

            Assert.True(published is not null, $"{loaded.RelativePath}: {code} is not in B3's catalogue.");
            Assert.Equal(published!.Name, loaded.File.FigureName);
        }
    }

    [Fact]
    public void Compiling_against_the_catalogue_produces_no_warnings()
    {
        var noisy = DomainFiles.Compiled
            .Where(kv => kv.Value.Warnings.Count > 0)
            .Select(kv => $"{kv.Key}: {string.Join("; ", kv.Value.Warnings)}")
            .ToList();

        Assert.Empty(noisy);
    }

    [Fact]
    public void Every_option_mapped_to_a_B3_domain_carries_a_code_B3_publishes()
    {
        var checkedOptions = 0;

        foreach (var code in DomainFiles.Compiled.Keys)
        {
            foreach (var field in DomainFiles.Template(code).AllFields())
            {
                if (field.B3Domain is not { } domainType) continue;

                var domain = Reference.Domain(domainType);
                Assert.True(domain.Count > 0, $"{code}: domain '{domainType}' is not in the export.");

                foreach (var option in field.Options)
                {
                    Assert.True(option.B3Code is not null,
                        $"{code}: {field.Path} option '{option.Code}' has no b3Code.");
                    Assert.Contains(domain, v => v.Code == option.B3Code && v.Enabled);
                    checkedOptions++;
                }
            }
        }

        // Guards the guard: if the mapping silently disappeared this test would pass vacuously.
        Assert.True(checkedOptions >= 30, $"Only {checkedOptions} option(s) are mapped to a B3 domain.");
    }

    [Fact]
    public void Underlying_classes_offered_are_classes_B3_actually_lists_for_COE()
    {
        var published = Reference.Underlyings
            .Where(u => u.InstrumentType == B3Reference.CoeInstrumentType)
            .Select(u => u.AssetClass)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var offered = DomainFiles.Template("COE001005").FindField("underlying.assetClass")!.Options;

        Assert.NotEmpty(offered);
        foreach (var option in offered)
            Assert.Contains(option.Code, published);
    }

    [Theory]
    // B3's label column is not punctuated consistently, and every one of these is a real row.
    [InlineData("01;COE001001 - Call", "COE001001", "Call")]
    [InlineData("60;COE001060- CCallSpread + VPutSpread", "COE001060", "CCallSpread + VPutSpread")]
    [InlineData("87;COE001087 CallSpread + CallSpread", "COE001087", "CallSpread + CallSpread")]
    [InlineData("83;COE001083 - COE de Crédito–Troca de Indexadores", "COE001083", "COE de Crédito–Troca de Indexadores")]
    [InlineData("78;COE001078 - COE de Crédito - CDS", "COE001078", "COE de Crédito - CDS")]
    public void The_figure_label_parses_whatever_punctuation_B3_used(string row, string code, string name)
    {
        var (parsedCode, parsedName) = B3Reference.SplitFigureLabel(row.Split(';')[1]);

        Assert.Equal(code, parsedCode);
        Assert.Equal(name, parsedName);
    }

    [Fact]
    public void A_row_carrying_only_a_name_yields_no_code()
    {
        // Rows 85 and 86 of the export name a figure without giving its code.
        var (code, name) = B3Reference.SplitFigureLabel("COE de Crédito – CDS com Amortização");

        Assert.Null(code);
        Assert.Equal("COE de Crédito – CDS com Amortização", name);
    }

    [Fact]
    public void Codes_are_never_longer_than_the_column_that_stores_them()
    {
        // A label parsed as one long "code" is how a bulk load into b3.Figure(nvarchar(20)) fails.
        foreach (var figure in Reference.Figures)
            Assert.True(figure.Code.Length <= 20, $"'{figure.Code}' is {figure.Code.Length} characters.");
    }

    // ----- the checks themselves --------------------------------------------------------

    private static DomainFile FileWith(FieldDto field, string figureCode = "COE001005", string figureName = "Call Spread") => new()
    {
        FigureCode = figureCode,
        FigureName = figureName,
        Sections = [new SectionDto { Key = "payoff", Kind = "tab", Fields = [field] }]
    };

    private static CompilationResult Compile(DomainFile file) =>
        new TemplateCompiler(Reference).Compile(file, new Dictionary<string, DomainFile>(), 1);

    [Fact]
    public void A_figure_code_B3_does_not_publish_fails_compilation()
    {
        var result = Compile(FileWith(
            new FieldDto { Key = "cap", DataType = "percent" },
            figureCode: "COE009999", figureName: "Invented"));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("COE009999") && e.Contains("catalogue"));
    }

    [Fact]
    public void A_figure_name_that_drifted_from_the_catalogue_warns()
    {
        var result = Compile(FileWith(
            new FieldDto { Key = "cap", DataType = "percent" },
            figureName: "Trava de alta"));

        Assert.True(result.Succeeded);
        Assert.Contains(result.Warnings, w => w.Contains("Call Spread"));
    }

    [Fact]
    public void An_option_code_that_is_not_in_the_named_domain_fails_compilation()
    {
        var result = Compile(FileWith(new FieldDto
        {
            Key = "basketType",
            DataType = "enum",
            B3Domain = "TIPO CESTA",
            Options = [new OptionDto { Code = "STANDARD", B3Code = "999" }]
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("999") && e.Contains("TIPO CESTA"));
    }

    [Fact]
    public void An_option_mapped_to_a_domain_but_missing_its_code_fails_compilation()
    {
        var result = Compile(FileWith(new FieldDto
        {
            Key = "basketType",
            DataType = "enum",
            B3Domain = "TIPO CESTA",
            Options = [new OptionDto { Code = "STANDARD" }]
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("no b3Code"));
    }

    [Fact]
    public void An_option_B3_has_disabled_warns_rather_than_failing()
    {
        // Code 12 of REMUNERADOR NO VENCIMENTO was disabled by B3 under ticket SDE-627.
        var result = Compile(FileWith(new FieldDto
        {
            Key = "maturityRemunerator",
            DataType = "enum",
            B3Domain = "REMUNERADOR NO VENCIMENTO",
            Options = [new OptionDto { Code = "PRE_LIN360", B3Code = "12" }]
        }));

        Assert.True(result.Succeeded);
        Assert.Contains(result.Warnings, w => w.Contains("disabled"));
    }

    [Fact]
    public void A_field_whose_type_contradicts_the_B3_dictionary_fails_compilation()
    {
        // C0000368 "Limitador de Alta" is NUMERICO(10,4).
        var result = Compile(FileWith(new FieldDto
        {
            Key = "cap",
            DataType = "date",
            B3FieldCode = "C0000368"
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("C0000368") && e.Contains("NUMERICO"));
    }

    [Fact]
    public void A_field_declaring_more_decimals_than_B3_registers_warns()
    {
        var result = Compile(FileWith(new FieldDto
        {
            Key = "cap",
            DataType = "percent",
            B3FieldCode = "C0000368",
            Decimals = 8
        }));

        Assert.True(result.Succeeded);
        Assert.Contains(result.Warnings, w => w.Contains("decimal"));
    }

    [Fact]
    public void An_unknown_B3_field_code_fails_compilation()
    {
        var result = Compile(FileWith(new FieldDto
        {
            Key = "cap",
            DataType = "percent",
            B3FieldCode = "C9999999"
        }));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("C9999999"));
    }

    [Fact]
    public void The_strategy_dictionary_and_underlying_master_are_loaded()
    {
        Assert.True(Reference.StrategyFields.Count > 5000);
        Assert.Equal("Limitador de Alta", Reference.StrategyField("C0000368")!.Name);
        Assert.Equal(4, Reference.StrategyField("C0000368")!.Decimals);

        // Tipo Cesta is a DOMINIO field, so the dictionary lists its accepted values.
        Assert.NotEmpty(Reference.StrategyField("C0001616")!.DomainValues);

        Assert.True(Reference.Underlyings.Count > 7000);
        // B3 registers the index as IBOVESPA; "IBOV" is a ticker convention, not a listed code.
        Assert.Contains(Reference.Underlyings, u => u.Code == "IBOVESPA");
        Assert.Contains(Reference.Underlyings, u => u.Code == "PETR4" && u.InstrumentType == "COE");
    }
}
