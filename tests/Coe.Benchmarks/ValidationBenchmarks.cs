using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Coe.Core.Templates;
using Coe.Core.Validation;
using Coe.Ingestion;

namespace Coe.Benchmarks;

/// <summary>
/// The validate endpoint is called on every keystroke in the booking screen, so its cost is a
/// user-visible number rather than a curiosity. This measures the part the platform controls:
/// the pass itself, with no HTTP and no database.
///
/// Run: <c>dotnet run -c Release --project tests/Coe.Benchmarks</c>
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
public class ValidationBenchmarks
{
    private FigureTemplate _template = null!;
    private string _templateJson = null!;
    private ValidationEngine _engine = null!;
    private JsonObject _values = null!;
    private string[] _changed = null!;

    [GlobalSetup]
    public void Setup()
    {
        var domain = LocateDomain();
        var set = new DomainFileLoader(domain).Load();
        var file = set.Figures.Single(f => f.File.FigureCode == "COE001005").File;

        var result = new TemplateCompiler().Compile(file, set.Fragments, 1);
        _template = result.Template ?? throw new InvalidOperationException(string.Join("; ", result.Errors));
        _templateJson = TemplateJson.Serialize(_template);
        _engine = new ValidationEngine();
        _changed = ["payoff.cap"];
        _values = Instance();
    }

    /// <summary>What the client sends on a keystroke: one changed path, everything else unchanged.</summary>
    [Benchmark(Description = "validate: field scope (one changed attribute)")]
    public int FieldScope() =>
        _engine.Validate(_template, _values, ValidationScope.Field, _changed).Messages.Count;

    /// <summary>The gate the API runs before writing.</summary>
    [Benchmark(Description = "validate: submit scope (whole instance)")]
    public int SubmitScope() =>
        _engine.Validate(_template, _values, ValidationScope.Submit).Messages.Count;

    /// <summary>What a template cache miss costs, and why versions are cached forever.</summary>
    [Benchmark(Description = "deserialize a stored template")]
    public int DeserializeTemplate() =>
        TemplateJson.Deserialize(_templateJson).Rules.Count;

    [Benchmark(Description = "recompute derived attributes")]
    public void ApplyComputed() => ComputedFields.Apply(_template, _values);

    private static JsonObject Instance() => new()
    {
        ["common"] = new JsonObject
        {
            ["issuerAccount"] = "40001",
            ["commercialName"] = "COE Call Spread IBOV 2 anos",
            ["issueDate"] = "2026-09-01",
            ["maturityDate"] = "2028-09-01",
            ["quantity"] = 1000,
            ["unitIssuePrice"] = 1000m,
            ["modality"] = "VNP",
            ["guaranteedCapital"] = 100m
        },
        ["underlying"] = new JsonObject
        {
            ["assetClass"] = "INDICES",
            ["asset"] = "IBOV",
            ["initialValue"] = 132000m,
            ["fixingWindow"] = "DATA_UNICA",
            ["quoteType"] = "FECHAMENTO",
            ["hasLookback"] = false
        },
        ["remuneration"] = new JsonObject
        {
            ["maturityRemunerator"] = "SEM_REMUNERACAO",
            ["hasCashFlow"] = false
        },
        ["terms"] = new JsonObject
        {
            ["baseApplication"] = 100m,
            ["issuerPosition"] = "COMPRADO",
            ["custodyRegime"] = "DEPOSITADO",
            ["cvmResolution8"] = false,
            ["earlyRedemption"] = "SEM_LIQUIDEZ",
            ["issuerCallClause"] = false
        },
        ["payoff"] = new JsonObject
        {
            ["strike"] = 100m,
            ["participation"] = 100m,
            ["cap"] = 25m
        }
    };

    private static string LocateDomain()
    {
        var configured = Environment.GetEnvironmentVariable("COE_DOMAIN_DIR");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "domain");
            if (Directory.Exists(Path.Combine(candidate, "figures"))) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the domain/ directory.");
    }
}

public static class Program
{
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(ValidationBenchmarks).Assembly)
            .Run(args, DefaultConfig.Instance.WithOptions(ConfigOptions.DisableOptimizationsValidator));
}
