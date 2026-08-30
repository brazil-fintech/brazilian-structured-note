using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Coe.Ingestion;

public sealed record LoadedDomainFile(DomainFile File, string RelativePath, string Hash);

public sealed record DomainFileSet(
    IReadOnlyDictionary<string, DomainFile> Fragments,
    IReadOnlyList<LoadedDomainFile> Figures,
    IReadOnlyList<string> Errors);

/// <summary>
/// Reads the domain files off disk. Figures live in <c>domain/figures/*.json</c>; reusable
/// blocks live in <c>domain/common/*.json</c> and are addressed as <c>common/&lt;name&gt;</c>.
///
/// The hash covers the figure file <em>and</em> every fragment it extends, so editing a shared
/// block re-issues a template version for each figure that inherits it — the alternative would
/// leave figures quietly running against a stale copy of the common block.
/// </summary>
public sealed class DomainFileLoader(string rootDirectory)
{
    public string RootDirectory { get; } = rootDirectory;

    public DomainFileSet Load()
    {
        var errors = new List<string>();
        var fragments = new Dictionary<string, DomainFile>(StringComparer.Ordinal);
        var fragmentHashes = new Dictionary<string, string>(StringComparer.Ordinal);

        var commonDir = Path.Combine(RootDirectory, "common");
        if (Directory.Exists(commonDir))
        {
            foreach (var path in Directory.EnumerateFiles(commonDir, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var id = "common/" + Path.GetFileNameWithoutExtension(path);
                if (TryRead(path, errors) is not { } read) continue;
                fragments[id] = read.File;
                fragmentHashes[id] = read.Hash;
            }
        }

        var figures = new List<LoadedDomainFile>();
        var figuresDir = Path.Combine(RootDirectory, "figures");
        if (!Directory.Exists(figuresDir))
        {
            errors.Add($"Figure directory not found: {figuresDir}");
            return new DomainFileSet(fragments, figures, errors);
        }

        foreach (var path in Directory.EnumerateFiles(figuresDir, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (TryRead(path, errors) is not { } read) continue;

            var composite = new StringBuilder(read.Hash);
            foreach (var id in read.File.Extends.Order(StringComparer.Ordinal))
                composite.Append('|').Append(id).Append('=').Append(fragmentHashes.GetValueOrDefault(id, "missing"));

            var hash = Sha256(composite.ToString());
            read.File.SourceHash = hash;
            figures.Add(read with { Hash = hash });
        }

        return new DomainFileSet(fragments, figures, errors);
    }

    private LoadedDomainFile? TryRead(string path, List<string> errors)
    {
        var relative = Path.GetRelativePath(RootDirectory, path).Replace('\\', '/');
        try
        {
            var text = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<DomainFile>(text, DomainFile.Json);
            if (file is null)
            {
                errors.Add($"{relative}: file is empty.");
                return null;
            }
            file.SourcePath = relative;
            var hash = Sha256(text);
            file.SourceHash = hash;
            return new LoadedDomainFile(file, relative, hash);
        }
        catch (JsonException ex)
        {
            errors.Add($"{relative}: invalid JSON — {ex.Message}");
            return null;
        }
        catch (IOException ex)
        {
            errors.Add($"{relative}: could not be read — {ex.Message}");
            return null;
        }
    }

    private static string Sha256(string content) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
