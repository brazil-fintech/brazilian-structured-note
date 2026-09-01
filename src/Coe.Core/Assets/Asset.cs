namespace Coe.Core.Assets;

public enum AssetStatus
{
    /// <summary>Saved with known problems; not registrable.</summary>
    Draft,

    /// <summary>Passed the full submit-time validation.</summary>
    Validated,

    /// <summary>Sent to B3 for registration.</summary>
    Registered,

    Cancelled
}

/// <summary>
/// A booked COE. The full attribute set lives in <see cref="ValuesJson"/>, shaped by the
/// figure template it was booked against; the columns duplicated here are the ones the asset
/// list filters and sorts on, so the grid never has to open the JSON.
/// </summary>
public sealed class Asset
{
    public Guid Id { get; set; }

    public required string FigureCode { get; set; }

    /// <summary>Template version the asset was booked against — re-validation is done against this one.</summary>
    public int TemplateVersion { get; set; }

    // --- denormalized common attributes, kept in sync from ValuesJson on every save ---

    /// <summary>Código IF, when B3 has issued one.</summary>
    public string? InstrumentCode { get; set; }

    public string? IsinCode { get; set; }

    /// <summary>Nome Fantasia.</summary>
    public required string CommercialName { get; set; }

    public string? IssuerAccount { get; set; }

    /// <summary>Data de Emissão.</summary>
    public DateOnly IssueDate { get; set; }

    /// <summary>Data de Vencimento.</summary>
    public DateOnly MaturityDate { get; set; }

    /// <summary>VNP (Capital Investido Garantido) or VNR (Perda Limitada ao Capital Investido).</summary>
    public string? Modality { get; set; }

    public string? UnderlyingClass { get; set; }
    public string? Underlying { get; set; }
    public long? Quantity { get; set; }
    public decimal? UnitIssuePrice { get; set; }

    /// <summary>Quantidade × Valor Unitário — the base for percentage-registered payoff amounts.</summary>
    public decimal? NotionalAmount { get; set; }

    public AssetStatus Status { get; set; } = AssetStatus.Draft;

    /// <summary>Every booked attribute, as the instance document the template describes.</summary>
    public required string ValuesJson { get; set; }

    /// <summary>Warnings accepted at save time, kept for audit.</summary>
    public string? WarningsJson { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>SQL Server rowversion, used for optimistic concurrency on edit.</summary>
    public byte[]? RowVersion { get; set; }

    /// <summary>The reference-date filter of the asset list: live on and between issue and maturity.</summary>
    public bool IsLiveOn(DateOnly referenceDate) =>
        IssueDate <= referenceDate && referenceDate <= MaturityDate;
}
