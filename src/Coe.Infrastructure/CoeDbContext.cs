using Coe.Core.Assets;
using Coe.Core.Figures;
using Microsoft.EntityFrameworkCore;

namespace Coe.Infrastructure;

/// <summary>
/// The MSSQL mapping. The schema itself is owned by the scripts in <c>db/</c> and applied by
/// <see cref="DatabaseBootstrapper"/> — EF is used for querying and change tracking only, so
/// the DDL a DBA reviews is the DDL that runs.
/// </summary>
public sealed class CoeDbContext(DbContextOptions<CoeDbContext> options) : DbContext(options)
{
    public DbSet<Figure> Figures => Set<Figure>();
    public DbSet<FigureTemplateRecord> FigureTemplates => Set<FigureTemplateRecord>();
    public DbSet<IngestionRun> IngestionRuns => Set<IngestionRun>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<UnderlyingRef> Underlyings => Set<UnderlyingRef>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Figure>(e =>
        {
            e.ToTable("Figure", "figure");
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.CommercialName).HasMaxLength(200);
            e.Property(x => x.Modalities).HasMaxLength(50).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.SourceFile).HasMaxLength(400);
            e.Property(x => x.SourceHash).HasMaxLength(80);
            e.HasMany(x => x.Templates).WithOne(x => x.Figure!).HasForeignKey(x => x.FigureCode);
        });

        b.Entity<FigureTemplateRecord>(e =>
        {
            e.ToTable("FigureTemplate", "figure");
            e.HasKey(x => x.Id);
            e.Property(x => x.FigureCode).HasMaxLength(20).IsRequired();
            e.Property(x => x.SchemaVersion).HasMaxLength(10).IsRequired();
            e.Property(x => x.TemplateJson).IsRequired();
            e.Property(x => x.SourceHash).HasMaxLength(80).IsRequired();
            e.Property(x => x.SourceFile).HasMaxLength(400);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.FigureCode, x.Version }).IsUnique();
        });

        b.Entity<IngestionRun>(e =>
        {
            e.ToTable("IngestionRun", "figure");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(30).IsRequired();
        });

        b.Entity<Asset>(e =>
        {
            e.ToTable("Asset", "asset");
            e.HasKey(x => x.Id);
            e.Property(x => x.FigureCode).HasMaxLength(20).IsRequired();
            e.Property(x => x.InstrumentCode).HasMaxLength(20);
            e.Property(x => x.IsinCode).HasMaxLength(12);
            e.Property(x => x.CommercialName).HasMaxLength(200).IsRequired();
            e.Property(x => x.IssuerAccount).HasMaxLength(20);
            e.Property(x => x.Modality).HasMaxLength(10);
            e.Property(x => x.UnderlyingClass).HasMaxLength(30);
            e.Property(x => x.Underlying).HasMaxLength(60);
            e.Property(x => x.UnitIssuePrice).HasPrecision(28, 8);
            e.Property(x => x.NotionalAmount).HasPrecision(28, 8);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ValuesJson).IsRequired();
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.MaturityDate, x.IssueDate });
            e.HasIndex(x => x.InstrumentCode).IsUnique().HasFilter("[InstrumentCode] IS NOT NULL");
        });

        b.Entity<Holiday>(e =>
        {
            e.ToTable("Holiday", "ref");
            e.HasKey(x => new { x.CalendarCode, x.HolidayDate });
            e.Property(x => x.CalendarCode).HasMaxLength(20);
            e.Property(x => x.Description).HasMaxLength(120);
        });

        b.Entity<UnderlyingRef>(e =>
        {
            e.ToTable("Underlying", "ref");
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.AssetClass).HasMaxLength(30).IsRequired();
        });
    }
}

public sealed class Holiday
{
    public required string CalendarCode { get; set; }
    public DateOnly HolidayDate { get; set; }
    public string? Description { get; set; }
}

public sealed class UnderlyingRef
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string AssetClass { get; set; }
    public bool IsActive { get; set; } = true;
}
