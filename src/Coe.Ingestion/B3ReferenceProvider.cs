using Microsoft.Extensions.Logging;

namespace Coe.Ingestion;

/// <summary>
/// Holds the reference data the platform is currently working from, and swaps it for a newly
/// downloaded set without a restart.
///
/// B3's exports used to be files someone committed, read once at start-up. They are now pulled
/// from CETIP's public directory while the process runs, so "the exports" is a value that
/// changes, and every reader has to see the change: the compiler that checks a figure against
/// the catalogue, and the importer that publishes the same rows to the database.
///
/// The swap is a single reference assignment. A pass that is already running keeps the set it
/// started with — a compile checked half against yesterday's catalogue and half against today's
/// would be a worse outcome than one checked wholly against either.
/// </summary>
public sealed class B3ReferenceProvider
{
    private readonly string _directory;
    private readonly ILogger<B3ReferenceProvider> _logger;
    private B3Reference _current;

    public B3ReferenceProvider(string directory, ILogger<B3ReferenceProvider> logger)
    {
        _directory = directory;
        _logger = logger;
        _current = B3Reference.Load(directory);
        LoadedUtc = DateTimeOffset.UtcNow;
        Report(_current);
    }

    /// <summary>The set in force. Read once at the top of a pass, never mid-pass.</summary>
    public B3Reference Current => Volatile.Read(ref _current);

    public DateTimeOffset LoadedUtc { get; private set; }

    public string Directory => _directory;

    /// <summary>Re-reads the directory and publishes the result. Returns the new set.</summary>
    public B3Reference Reload()
    {
        var loaded = B3Reference.Load(_directory);
        Volatile.Write(ref _current, loaded);
        LoadedUtc = DateTimeOffset.UtcNow;
        Report(loaded);
        return loaded;
    }

    private void Report(B3Reference reference)
    {
        foreach (var error in reference.Errors)
            _logger.LogWarning("B3 reference: {Message}", error);

        _logger.LogInformation(
            "B3 reference loaded from {Directory} (as of {AsOf}): {Figures} figure(s), {Underlyings} underlying(s), "
            + "{DataFields} derivative field(s), {MappedFigures} figure(s) with a published attribute list",
            _directory, reference.AsOf ?? "unknown", reference.Figures.Count, reference.Underlyings.Count,
            reference.DerivativeFields.Fields.Count, reference.DerivativeFields.FigureCodes.Count);
    }
}
