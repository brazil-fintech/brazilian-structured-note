using Coe.Core.Templates;
using Coe.Ingestion;
using Xunit;

namespace Coe.Tests;

/// <summary>Loads and compiles the repository's real domain files, once per test run.</summary>
public static class DomainFiles
{
    // Declared first: static initializers run in order, and the loaders below read it.
    public static string Directory { get; } = Locate();

    private static readonly Lazy<DomainFileSet> LoadedSet = new(() => new DomainFileLoader(Directory).Load());

    /// <summary>B3's published exports, so the suite compiles the way the worker does.</summary>
    public static B3Reference Reference { get; } = B3Reference.Load(Path.Combine(RepositoryRoot(), "reference", "b3"));

    private static readonly Lazy<IReadOnlyDictionary<string, CompilationResult>> CompiledFigures =
        new(() => Set.Figures.ToDictionary(
            f => f.File.FigureCode!,
            f => new TemplateCompiler(Reference).Compile(f.File, Set.Fragments, 1),
            StringComparer.Ordinal));

    public static DomainFileSet Set => LoadedSet.Value;

    public static IReadOnlyDictionary<string, CompilationResult> Compiled => CompiledFigures.Value;

    public static FigureTemplate Template(string figureCode)
    {
        var result = Compiled[figureCode];
        Assert.True(result.Succeeded, $"{figureCode} failed to compile: {string.Join("; ", result.Errors)}");
        return result.Template!;
    }

    /// <summary>Repository root, found by walking up from the test output directory.</summary>
    public static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.Directory.Exists(Path.Combine(dir.FullName, "db")) &&
                System.IO.Directory.Exists(Path.Combine(dir.FullName, "domain")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root from the test output path.");
    }

    private static string Locate()
    {
        // Lets a build whose output sits outside the repository still find the catalog.
        var configured = Environment.GetEnvironmentVariable("COE_DOMAIN_DIR");
        if (!string.IsNullOrWhiteSpace(configured) && System.IO.Directory.Exists(Path.Combine(configured, "figures")))
            return configured;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "domain");
            if (System.IO.Directory.Exists(Path.Combine(candidate, "figures"))) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the domain/ directory from the test output path.");
    }
}
