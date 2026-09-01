namespace Coe.Core.Figures;

/// <summary>Lifecycle of a figure inside the platform.</summary>
public enum FigureStatus
{
    /// <summary>Compiled but not yet released for booking.</summary>
    Pending,

    /// <summary>Released: the figure is offered when creating a new asset.</summary>
    Enabled,

    /// <summary>Compilation failed, or the figure was pulled. Existing assets stay readable.</summary>
    Quarantined,

    /// <summary>Withdrawn by B3 or by the desk; not offered for new assets.</summary>
    Retired
}

/// <summary>A B3 payoff figure known to the platform.</summary>
public sealed class Figure
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? CommercialName { get; set; }
    public string? DescriptionPt { get; set; }
    public string? DescriptionEn { get; set; }

    /// <summary>Comma-separated modalities the figure supports (VNP, VNR).</summary>
    public string Modalities { get; set; } = string.Empty;

    public FigureStatus Status { get; set; } = FigureStatus.Pending;

    /// <summary>Version of the currently active template; null while no template compiled cleanly.</summary>
    public int? ActiveTemplateVersion { get; set; }

    public string? SourceFile { get; set; }
    public string? SourceHash { get; set; }

    /// <summary>Populated when <see cref="Status"/> is <see cref="FigureStatus.Quarantined"/>.</summary>
    public string? LastError { get; set; }

    public DateTimeOffset FirstSeenUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public DateTimeOffset? EnabledUtc { get; set; }

    public List<FigureTemplateRecord> Templates { get; set; } = [];

    public IEnumerable<string> ModalityList() =>
        Modalities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>One immutable compiled template version. New content never overwrites an old version.</summary>
public sealed class FigureTemplateRecord
{
    public long Id { get; set; }
    public required string FigureCode { get; set; }
    public int Version { get; set; }
    public string SchemaVersion { get; set; } = Templates.TemplateSchema.CurrentVersion;

    /// <summary>The serialized <see cref="Templates.FigureTemplate"/>.</summary>
    public required string TemplateJson { get; set; }

    public required string SourceHash { get; set; }
    public string? SourceFile { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public string? CreatedBy { get; set; }

    public Figure? Figure { get; set; }
}

/// <summary>
/// What a desk can actually do with a figure B3 publishes. <see cref="FigureStatus"/> describes a
/// figure the platform knows; this describes every figure in B3's catalogue, including the ones
/// nobody has written a domain file for yet.
/// </summary>
public enum FigureAvailability
{
    /// <summary>B3 publishes the figure; this platform has no domain file for it, so it cannot be booked.</summary>
    NotConfigured,

    /// <summary>A domain file exists and compiled, but the figure has not been released for booking.</summary>
    Pending,

    /// <summary>A domain file exists and does not compile. <see cref="CatalogueFigure.LastError"/> says why.</summary>
    Quarantined,

    /// <summary>Withdrawn; existing assets stay readable, no new ones are offered.</summary>
    Retired,

    /// <summary>A template is published and released: the figure can be booked.</summary>
    Available
}

/// <summary>
/// One row of B3's published figure catalogue, paired with what this platform can do about it.
///
/// The two sides are joined rather than intersected on purpose: a figure B3 publishes that has no
/// domain file still belongs in the picker, greyed out, so the gap between the 88 figures B3 ships
/// and the handful modelled here is visible instead of silently absent. The join also keeps a
/// platform figure whose code is missing from the catalogue — an export not yet loaded, or a code
/// B3 retired — rather than dropping it from the only screen that lists figures.
/// </summary>
public sealed class CatalogueFigure
{
    public required string Code { get; set; }

    /// <summary>The name as B3 publishes it; null when the figure is not in the loaded catalogue.</summary>
    public string? B3Name { get; set; }

    /// <summary>True when B3 calculates settlement for the figure.</summary>
    public bool CalculatedByB3 { get; set; }

    /// <summary>True when the code appears in the loaded <c>b3.Figure</c> export.</summary>
    public bool InB3Catalogue { get; set; }

    /// <summary>The platform's figure, when a domain file has been compiled for this code.</summary>
    public Figure? Figure { get; set; }

    /// <summary>The platform's name where one exists, otherwise B3's registered name.</summary>
    public string Name => Figure?.Name ?? B3Name ?? Code;

    public string? LastError => Figure?.LastError;

    /// <summary>True only when the figure has an active template and is released.</summary>
    public bool Bookable => Figure is { Status: FigureStatus.Enabled, ActiveTemplateVersion: not null };

    public FigureAvailability Availability => Figure?.Status switch
    {
        null => FigureAvailability.NotConfigured,
        FigureStatus.Enabled when Figure.ActiveTemplateVersion is null => FigureAvailability.Pending,
        FigureStatus.Enabled => FigureAvailability.Available,
        FigureStatus.Pending => FigureAvailability.Pending,
        FigureStatus.Quarantined => FigureAvailability.Quarantined,
        FigureStatus.Retired => FigureAvailability.Retired,
        _ => FigureAvailability.NotConfigured
    };
}
