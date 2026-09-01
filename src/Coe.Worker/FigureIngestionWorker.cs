using Coe.Infrastructure;
using Coe.Ingestion;

namespace Coe.Worker;

/// <summary>
/// Watches the domain files and keeps the figure catalog in step with them.
///
/// It runs once at startup, then on the configured interval, and — when
/// <see cref="IngestionOptions.WatchFileSystem"/> is on — as soon as a file changes, so a
/// figure B3 has just published is bookable within seconds of landing in <c>domain/figures/</c>.
/// Writes are debounced: editors save partial files, and compiling one would quarantine a
/// figure that is merely mid-save.
/// </summary>
public sealed class FigureIngestionWorker(
    IServiceProvider services,
    IngestionOptions options,
    ILogger<FigureIngestionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);
    private readonly SemaphoreSlim _wake = new(0, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var watcher = options.WatchFileSystem ? CreateWatcher() : null;

        await ImportReferenceDataAsync(stoppingToken);

        logger.LogInformation(
            "Figure ingestion started. Directory={Directory} Interval={Interval} Watch={Watch} AutoEnable={AutoEnable}",
            options.DomainDirectory, options.Interval, options.WatchFileSystem, options.AutoEnableNewFigures);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);

            // Wake on either the interval or a file change, whichever comes first.
            using var cycle = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            try
            {
                await _wake.WaitAsync(options.Interval, cycle.Token);
                await Task.Delay(Debounce, cycle.Token);
                DrainPendingSignals();
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Publishes B3's exports into the reference tables before the first compile. The compiler
    /// already checks against the files; this is what puts the same data behind the API, so the
    /// underlying picker and the booking screen agree with what ingestion validated.
    /// </summary>
    private async Task ImportReferenceDataAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var importer = scope.ServiceProvider.GetRequiredService<B3ReferenceImporter>();
            await importer.ImportAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Stale reference data is better than no worker; the next restart retries.
            logger.LogError(ex, "Could not load the B3 reference exports; continuing with what is already stored");
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var ingestion = scope.ServiceProvider.GetRequiredService<FigureIngestionService>();
            await ingestion.RunAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A transient database outage must not take the worker down; the next pass retries.
            logger.LogError(ex, "Ingestion pass failed; retrying on the next cycle");
        }
    }

    private FileSystemWatcher CreateWatcher()
    {
        Directory.CreateDirectory(options.DomainDirectory);
        var watcher = new FileSystemWatcher(options.DomainDirectory, "*.json")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
        };
        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Renamed += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        logger.LogDebug("Domain file {Change}: {Name}", e.ChangeType, e.Name);
        Signal();
    }

    private void Signal()
    {
        // A full semaphore already means "there is work"; extra signals add nothing.
        try { _wake.Release(); } catch (SemaphoreFullException) { }
    }

    private void DrainPendingSignals()
    {
        while (_wake.CurrentCount > 0) _wake.Wait(0);
    }

    public override void Dispose()
    {
        _wake.Dispose();
        base.Dispose();
    }
}
