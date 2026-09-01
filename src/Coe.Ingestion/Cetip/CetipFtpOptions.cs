namespace Coe.Ingestion.Cetip;

/// <summary>
/// Where B3's public exports are fetched from, and how often.
///
/// The defaults are the address the *ENVIAR ARQUIVOS* layouts print next to the fields whose
/// domains live in these files: anonymous FTP on <c>ftp.cetip.com.br</c>, directory
/// <c>/Public</c>. Everything here can be overridden per environment; pointing
/// <see cref="LocalMirrorDirectory"/> at a folder replaces the network entirely, which is how
/// the tests run and how a desk behind a firewall mirrors the files once and shares them.
/// </summary>
public sealed class CetipFtpOptions
{
    public const string SectionName = "Cetip";

    /// <summary>Fetch at all. Off leaves the committed copies under <c>reference/b3/</c> in place.</summary>
    public bool Enabled { get; set; } = true;

    public string Host { get; set; } = "ftp.cetip.com.br";
    public int Port { get; set; } = 21;

    /// <summary>The public directory; the manuals write it <c>/Public</c>.</summary>
    public string Directory { get; set; } = "/Public";

    public string User { get; set; } = "anonymous";

    /// <summary>Anonymous FTP takes an e-mail address as the password by convention.</summary>
    public string Password { get; set; } = "anonymous@";

    /// <summary>Explicit TLS (<c>AUTH TLS</c>). Off: the published links are plain <c>ftp://</c>.</summary>
    public bool UseSsl { get; set; }

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Fetch once as the worker starts, before the first compile.</summary>
    public bool SyncOnStartup { get; set; } = true;

    /// <summary>
    /// Floor between two syncs. The ingestion loop wakes every few minutes to notice a domain
    /// file edit; pulling tens of megabytes from B3 at that rate would be rude and pointless,
    /// since the exports are published once a day.
    /// </summary>
    public TimeSpan MinimumInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// How many days back to probe for a dated file when the directory cannot be listed.
    /// Long weekends and holidays mean the newest export can be several days old.
    /// </summary>
    public int MaxLookbackDays { get; set; } = 10;

    /// <summary>
    /// Export names to fetch, from <see cref="CetipPublicFiles"/>. Empty means the ones the
    /// platform reads; naming one explicitly also pulls the registers it skips by default.
    /// </summary>
    public List<string> Exports { get; set; } = [];

    /// <summary>
    /// A local folder holding the same dated files. Set it and nothing connects to B3 — the
    /// folder is read instead, with the same newest-file selection.
    /// </summary>
    public string? LocalMirrorDirectory { get; set; }

    /// <summary>
    /// Overwrite a committed export with a download whose date is older than the one on disk.
    /// Off, because a listing that comes back short — a partial mirror, a directory mid-publish —
    /// must not roll the platform's reference data backwards.
    /// </summary>
    public bool AllowOlder { get; set; }
}
