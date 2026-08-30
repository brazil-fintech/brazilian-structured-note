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
