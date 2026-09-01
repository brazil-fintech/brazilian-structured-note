using Coe.Core.Templates;
using Coe.Ingestion;
using Xunit;

namespace Coe.Tests;

/// <summary>
/// The ingestion worker refuses to publish a template that does not compile, so these tests
/// are the guard that stops a bad domain-file edit from reaching the booking screen.
/// </summary>
public class TemplateCompilerTests
{
    [Fact]
    public void Every_domain_file_loads_without_errors() =>
        Assert.Empty(DomainFiles.Set.Errors);

    [Fact]
    public void At_least_the_documented_figures_are_present() =>
        Assert.True(DomainFiles.Set.Figures.Count >= 10,
            $"Expected the documented payoff catalog; found {DomainFiles.Set.Figures.Count} figure file(s).");

    [Fact]
    public void Every_figure_compiles()
    {
        var failures = DomainFiles.Compiled
            .Where(kv => !kv.Value.Succeeded)
            .Select(kv => $"{kv.Key}: {string.Join("; ", kv.Value.Errors)}")
            .ToList();

        Assert.Empty(failures);
    }

    [Fact]
    public void Figure_codes_are_unique()
    {
        var duplicates = DomainFiles.Set.Figures
            .GroupBy(f => f.File.FigureCode, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key!)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Every_template_starts_with_a_common_section()
    {
        foreach (var code in DomainFiles.Compiled.Keys)
        {
            var template = DomainFiles.Template(code);
            Assert.Equal(SectionKind.Common, template.Sections[0].Kind);
        }
    }

    [Fact]
    public void Every_rule_target_points_at_something_the_form_renders()
    {
        foreach (var code in DomainFiles.Compiled.Keys)
        {
            var template = DomainFiles.Template(code);
            var paths = template.AllFields().Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
            var sections = template.Sections.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);

            foreach (var rule in template.Rules)
                foreach (var target in rule.Targets)
                    Assert.True(paths.Contains(target) || sections.Contains(target),
                        $"{code}: rule '{rule.Id}' targets '{target}', which is neither a field nor a section.");
        }
    }

    [Fact]
    public void Rules_declare_the_attributes_they_read()
    {
        foreach (var code in DomainFiles.Compiled.Keys)
        {
            var template = DomainFiles.Template(code);
            foreach (var rule in template.Rules.Where(r => r.Assert is not null))
                Assert.NotEmpty(rule.DependsOn);
        }
    }

    [Fact]
    public void Bare_names_are_resolved_to_absolute_paths()
    {
        // Authors write `cap > 0`; the browser must receive `payoff.cap`.
        var template = DomainFiles.Template("COE001005");
        var rule = template.Rules.Single(r => r.Id == "callspread.cap-positive");
        Assert.Contains("payoff.cap", rule.DependsOn);
    }

    [Fact]
    public void Fragments_are_merged_into_the_figure()
    {
        var template = DomainFiles.Template("COE001005");
        Assert.NotNull(template.FindField("common.issueDate"));
        Assert.NotNull(template.FindField("underlying.assetClass"));
        Assert.NotNull(template.FindField("payoff.cap"));
    }

    [Fact]
    public void A_figure_can_override_an_inherited_field()
    {
        // The shark fin narrows the generic barrier direction down to "up" only.
        var template = DomainFiles.Template("COE001003");
        var direction = template.FindField("barriers.barrierDirection");
        Assert.NotNull(direction);
        Assert.Single(direction!.Options);
        Assert.Equal("ALTA", direction.Options[0].Code);
    }

    [Fact]
    public void Repeating_sections_expose_columns_not_fields()
    {
        var template = DomainFiles.Template("COE001005");
        var cashflows = template.FindSection("cashflows");
        Assert.NotNull(cashflows);
        Assert.True(cashflows!.Repeating);
        Assert.Empty(cashflows.Fields);
        Assert.NotEmpty(cashflows.ItemFields);
        Assert.Equal("cashflows[].paymentDate", cashflows.ItemFields[0].Path);
    }

    [Fact]
    public void Serialized_templates_round_trip()
    {
        foreach (var code in DomainFiles.Compiled.Keys)
        {
            var template = DomainFiles.Template(code);
            var json = TemplateJson.Serialize(template);
            var back = TemplateJson.Deserialize(json);

            Assert.Equal(template.FigureCode, back.FigureCode);
            Assert.Equal(template.Sections.Count, back.Sections.Count);
            Assert.Equal(template.Rules.Count, back.Rules.Count);
            Assert.Equal(TemplateJson.Serialize(back), json);
        }
    }

    [Fact]
    public void An_unknown_attribute_fails_compilation()
    {
        var file = new DomainFile
        {
            FigureCode = "TEST001",
            FigureName = "Test",
            Sections =
            [
                new SectionDto
                {
                    Key = "payoff",
                    Kind = "tab",
                    Fields = [new FieldDto { Key = "cap", DataType = "percent" }]
                }
            ],
            Rules =
            [
                new RuleDto
                {
                    Id = "test.typo",
                    Targets = ["payoff.cap"],
                    Assert = "capp > 0",
                    Message = new LocalizedTextDto { Pt = "x" }
                }
            ]
        };

        var result = new TemplateCompiler().Compile(file, new Dictionary<string, DomainFile>(), 1);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("capp"));
    }

    [Fact]
    public void A_rule_with_no_target_fails_compilation()
    {
        var file = new DomainFile
        {
            FigureCode = "TEST002",
            FigureName = "Test",
            Sections = [new SectionDto { Key = "payoff", Fields = [new FieldDto { Key = "cap", DataType = "percent" }] }],
            Rules = [new RuleDto { Id = "test.orphan", Assert = "cap > 0", Message = new LocalizedTextDto { Pt = "x" } }]
        };

        var result = new TemplateCompiler().Compile(file, new Dictionary<string, DomainFile>(), 1);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("no targets"));
    }
}
