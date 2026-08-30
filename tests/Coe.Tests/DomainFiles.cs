using Coe.Core.Templates;
using Coe.Ingestion;
using Xunit;

namespace Coe.Tests;

/// <summary>Loads and compiles the repository's real domain files, once per test run.</summary>
public static class DomainFiles
{
    private static readonly Lazy<DomainFileSet> LoadedSet = new(() => new DomainFileLoader(Directory).Load());
    private static readonly Lazy<IReadOnlyDictionary<string, CompilationResult>> CompiledFigures =
        new(() => Set.Figures.ToDictionary(
            f => f.File.FigureCode!,
            f => new TemplateCompiler().Compile(f.File, Set.Fragments, 1),
            StringComparer.Ordinal));

    public static string Directory { get; } = Locate();

    public static DomainFileSet Set => LoadedSet.Value;

    public static IReadOnlyDictionary<string, CompilationResult> Compiled => CompiledFigures.Value;

    public static FigureTemplate Template(string figureCode)
    {
        var result = Compiled[figureCode];
        Assert.True(result.Succeeded, $"{figureCode} failed to compile: {string.Join("; ", result.Errors)}");
        return result.Template!;
    }

    private static string Locate()
    {
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
