using Coe.Core.Assets;
using Coe.Core.Figures;
using Coe.Core.Templates;
using Coe.Core.Text;
using Coe.Infrastructure;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Coe.Tests;

/// <summary>
/// Exercises the ADO.NET layer against a real SQL Server. These cover the behaviour that only
/// exists in the database — the filtered unique index, rowversion concurrency, the reference-date
/// predicate, the schema scripts themselves — none of which an in-memory substitute would test.
/// </summary>
[Collection("sqlserver")]
public class DatabaseTests(SqlServerFixture sql)
{
    // ----- schema and figures --------------------------------------------------------

    [SqlServerFact]
    public async Task The_schema_scripts_create_a_usable_database()
    {
        // 002_reference_data.sql seeded the holiday calendar the date rules depend on.
        await sql.Calendar.EnsureLoadedAsync("BRASIL");

        Assert.False(sql.Calendar.IsBusinessDay("BRASIL", new DateOnly(2026, 12, 25)));  // Natal
        Assert.False(sql.Calendar.IsBusinessDay("BRASIL", new DateOnly(2026, 9, 5)));    // Saturday
        Assert.True(sql.Calendar.IsBusinessDay("BRASIL", new DateOnly(2026, 9, 1)));
    }

    [SqlServerFact]
    public async Task A_figure_can_be_inserted_then_updated_through_the_same_upsert()
    {
        var code = $"TEST{Random.Shared.Next(100000, 999999)}";
        var figure = new Figure
        {
            Code = code,
            Name = "First",
            Modalities = "VNP",
            Status = FigureStatus.Pending,
            FirstSeenUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        await sql.Catalog.UpsertAsync(figure);
        Assert.Equal("First", (await sql.Catalog.GetAsync(code))!.Name);

        figure.Name = "Second";
        figure.Status = FigureStatus.Enabled;
        await sql.Catalog.UpsertAsync(figure);

        var reloaded = await sql.Catalog.GetAsync(code);
        Assert.Equal("Second", reloaded!.Name);
        Assert.Equal(FigureStatus.Enabled, reloaded.Status);
    }

    [SqlServerFact]
    public async Task Only_enabled_figures_are_offered_for_booking()
    {
        var code = $"TEST{Random.Shared.Next(100000, 999999)}";
        await sql.Catalog.UpsertAsync(new Figure
        {
            Code = code,
            Name = "Quarantined figure",
            Modalities = "VNP",
            Status = FigureStatus.Quarantined,
            FirstSeenUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        });

        Assert.DoesNotContain(await sql.Catalog.ListAsync(enabledOnly: true), f => f.Code == code);
        Assert.Contains(await sql.Catalog.ListAsync(enabledOnly: false), f => f.Code == code);
    }

    [SqlServerFact]
    public async Task Publishing_a_template_version_stands_the_previous_one_down()
    {
        var figure = await sql.SeedFigureAsync($"TEST{Random.Shared.Next(100000, 999999)}");
        var template = DomainFiles.Template("COE001005");

        for (var version = 1; version <= 3; version++)
        {
            await sql.Catalog.AddTemplateVersionAsync(new FigureTemplateRecord
            {
                FigureCode = figure.Code,
                Version = version,
                TemplateJson = TemplateJson.Serialize(template),
                SourceHash = $"sha256:{version}",
                IsActive = true,
                CreatedUtc = DateTimeOffset.UtcNow
            });
        }

        // The filtered unique index allows one active row per figure; getting this wrong would
        // throw here rather than quietly serve two templates.
        Assert.Equal(3, await sql.Catalog.LatestTemplateVersionAsync(figure.Code));
        Assert.Equal(3, (await sql.Catalog.GetActiveTemplateAsync(figure.Code))!.Version);

        var older = await sql.Catalog.GetTemplateAsync(figure.Code, 1);
        Assert.NotNull(older);
        Assert.False(older!.IsActive);
    }

    [SqlServerFact]
    public async Task A_stored_template_round_trips_through_the_database()
    {
        var figure = await sql.SeedFigureAsync($"TEST{Random.Shared.Next(100000, 999999)}");
        var original = DomainFiles.Template("COE001022");

        await sql.Catalog.AddTemplateVersionAsync(new FigureTemplateRecord
        {
            FigureCode = figure.Code,
            Version = 1,
            TemplateJson = TemplateJson.Serialize(original),
            SourceHash = "sha256:abc",
            IsActive = true,
            CreatedUtc = DateTimeOffset.UtcNow
        });

        var stored = TemplateJson.Deserialize((await sql.Catalog.GetActiveTemplateAsync(figure.Code))!.TemplateJson);

        Assert.Equal(original.Sections.Count, stored.Sections.Count);
        Assert.Equal(original.Rules.Count, stored.Rules.Count);
        Assert.Equal(TemplateJson.Serialize(original), TemplateJson.Serialize(stored));
    }

    // ----- assets --------------------------------------------------------------------

    private async Task<Asset> NewAssetAsync(
        string figureCode, string issue = "2026-09-01", string maturity = "2028-09-01", string? instrumentCode = null)
    {
        await sql.SeedFigureAsync(figureCode);
        return new Asset
        {
            Id = Guid.CreateVersion7(),
            FigureCode = figureCode,
            TemplateVersion = 1,
            CommercialName = "COE Call Spread IBOV",
            InstrumentCode = instrumentCode,
            IssueDate = DateOnly.Parse(issue),
            MaturityDate = DateOnly.Parse(maturity),
            Modality = "VNP",
            UnderlyingClass = "INDICES",
            Underlying = "IBOV",
            Quantity = 1000,
            UnitIssuePrice = 1000m,
            NotionalAmount = 1_000_000m,
            Status = AssetStatus.Validated,
            ValuesJson = """{"common":{"quantity":1000}}""",
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
    }

    [SqlServerFact]
    public async Task An_asset_survives_a_save_and_reload()
    {
        var figureCode = $"TEST{Random.Shared.Next(100000, 999999)}";
        var asset = await NewAssetAsync(figureCode);

        var rowVersion = await sql.Assets.AddAsync(asset);
        Assert.NotNull(rowVersion);

        var reloaded = await sql.Assets.GetAsync(asset.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(asset.CommercialName, reloaded!.CommercialName);
        Assert.Equal(asset.IssueDate, reloaded.IssueDate);
        Assert.Equal(1_000_000m, reloaded.NotionalAmount);
        Assert.Equal(AssetStatus.Validated, reloaded.Status);
        Assert.Equal(asset.ValuesJson, reloaded.ValuesJson);
    }

    [SqlServerFact]
    public async Task The_reference_date_filter_keeps_only_live_assets()
    {
        var figureCode = $"TEST{Random.Shared.Next(100000, 999999)}";
        var asset = await NewAssetAsync(figureCode, "2026-09-01", "2028-09-01");
        await sql.Assets.AddAsync(asset);

        async Task<int> CountOn(string date) =>
            (await sql.Assets.SearchAsync(new AssetQuery
            {
                ReferenceDate = DateOnly.Parse(date),
                FigureCode = figureCode
            })).Total;

        Assert.Equal(1, await CountOn("2026-09-01"));   // issue date is inclusive
        Assert.Equal(1, await CountOn("2027-06-15"));
        Assert.Equal(1, await CountOn("2028-09-01"));   // maturity is inclusive
        Assert.Equal(0, await CountOn("2026-08-31"));   // not yet issued
        Assert.Equal(0, await CountOn("2028-09-02"));   // matured
    }

    [SqlServerFact]
    public async Task The_list_carries_the_figure_name_and_the_unpaged_total()
    {
        var figureCode = $"TEST{Random.Shared.Next(100000, 999999)}";
        for (var i = 0; i < 5; i++)
            await sql.Assets.AddAsync(await NewAssetAsync(figureCode));

        var page = await sql.Assets.SearchAsync(new AssetQuery
        {
            ReferenceDate = new DateOnly(2027, 1, 1),
            FigureCode = figureCode,
            PageSize = 2
        });

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(5, page.Total);                          // the total is unpaged
        Assert.Equal("Call Spread", page.Items[0].FigureName); // joined, not a second query
    }

    [SqlServerFact]
    public async Task A_stale_rowversion_is_rejected_rather_than_overwriting()
    {
        var figureCode = $"TEST{Random.Shared.Next(100000, 999999)}";
        var asset = await NewAssetAsync(figureCode);
        var first = await sql.Assets.AddAsync(asset);

        asset.CommercialName = "Edited once";
        var second = await sql.Assets.UpdateAsync(asset, first);
        Assert.NotEqual(Convert.ToBase64String(first!), Convert.ToBase64String(second!));

        // Someone else's session still holds the version from before that edit.
        asset.CommercialName = "Edited from a stale copy";
        await Assert.ThrowsAsync<AssetConcurrencyException>(() => sql.Assets.UpdateAsync(asset, first));

        Assert.Equal("Edited once", (await sql.Assets.GetAsync(asset.Id))!.CommercialName);
    }

    [SqlServerFact]
    public async Task Instrument_codes_are_unique_across_assets()
    {
        var figureCode = $"TEST{Random.Shared.Next(100000, 999999)}";
        var code = $"IF{Random.Shared.Next(100000, 999999)}";

        var first = await NewAssetAsync(figureCode, instrumentCode: code);
        await sql.Assets.AddAsync(first);

        Assert.True(await sql.Assets.InstrumentCodeTakenAsync(code, exceptAssetId: null));
        // An asset does not collide with itself, or editing it would be impossible.
        Assert.False(await sql.Assets.InstrumentCodeTakenAsync(code, exceptAssetId: first.Id));
        Assert.False(await sql.Assets.InstrumentCodeTakenAsync($"IF{Random.Shared.Next(100000, 999999)}", null));
    }

    [SqlServerFact]
    public async Task Free_text_search_covers_the_columns_a_desk_would_type_into()
    {
        var figureCode = $"TEST{Random.Shared.Next(100000, 999999)}";
        var marker = $"Sharkfin{Random.Shared.Next(100000, 999999)}";

        var asset = await NewAssetAsync(figureCode);
        asset.CommercialName = $"COE {marker} USDBRL";
        await sql.Assets.AddAsync(asset);

        var found = await sql.Assets.SearchAsync(new AssetQuery
        {
            ReferenceDate = new DateOnly(2027, 1, 1),
            Search = marker
        });

        Assert.Equal(1, found.Total);
        Assert.Contains(marker, found.Items[0].CommercialName, StringComparison.Ordinal);
    }

    // ----- stored upload files -------------------------------------------------------

    /// <summary>A stored generation, with bytes that are not valid UTF-8 on purpose.</summary>
    private static StoredClearingFileSet ClearingSet(Guid assetId, string figureCode, params string[] operations) =>
        new(
            Id: Guid.Empty,
            AssetId: assetId,
            FigureCode: figureCode,
            TemplateVersion: 1,
            ParticipantName: "BANCO EXEMPLO",
            FileDate: new DateOnly(2026, 9, 2),
            Notes: ["Registro COE: 12 attribute(s) in the variable-data record."],
            GeneratedUtc: DateTimeOffset.UtcNow,
            GeneratedBy: "desk@example.com",
            Files: operations.Select(operation =>
            {
                // 0xE7 is "ç" in the encoding CETIP reads and an invalid byte in UTF-8: if the
                // column or the round trip ever became text, this is what would break.
                var content = Windows1252.Encode($"COE  1{operation}CONFIRMA\u00c7\u00c3O\r\n");
                return new StoredClearingFile(
                    Guid.Empty, $"4.8.1 {operation}", operation, $"COE_{operation}.txt", 1,
                    content, ClearingFileRepository.Hash(content));
            }).ToList());

    [SqlServerFact]
    public async Task Generated_files_are_stored_and_come_back_byte_for_byte()
    {
        var figureCode = $"TEST{Random.Shared.Next(100000, 999999)}";
        var asset = await NewAssetAsync(figureCode);
        await sql.Assets.AddAsync(asset);

        var set = ClearingSet(asset.Id, figureCode, "0001", "FLUX");
        var stored = await sql.ClearingFiles.AddAsync(set);

        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.All(stored.Files, file => Assert.NotEqual(Guid.Empty, file.Id));

        var registration = stored.Files.Single(f => f.Operation == "0001");
        var reloaded = await sql.ClearingFiles.GetFileAsync(asset.Id, registration.Id);

        // The whole point of storing the bytes: what comes back is the upload, not a re-encoding
        // of a preview of it.
        Assert.Equal(registration.Content, reloaded!.Content);
        Assert.Equal(registration.ContentHash, reloaded.ContentHash);
        Assert.Equal("COE_0001.txt", reloaded.FileName);
    }

    [SqlServerFact]
    public async Task The_history_lists_generations_newest_first_and_keeps_its_notes()
    {
        var figureCode = $"TEST{Random.Shared.Next(100000, 999999)}";
        var asset = await NewAssetAsync(figureCode);
        await sql.Assets.AddAsync(asset);

        var older = ClearingSet(asset.Id, figureCode, "0001") with
        {
            GeneratedUtc = DateTimeOffset.UtcNow.AddDays(-1)
        };
        await sql.ClearingFiles.AddAsync(older);
        await sql.ClearingFiles.AddAsync(ClearingSet(asset.Id, figureCode, "0001", "FLUX"));

        var history = await sql.ClearingFiles.ListAsync(asset.Id);

        Assert.Equal(2, history.Count);
        Assert.Equal(2, history[0].Files.Count);          // newest first
        Assert.Single(history[1].Files);
        Assert.Equal("BANCO EXEMPLO", history[0].ParticipantName);
        Assert.Equal(new DateOnly(2026, 9, 2), history[0].FileDate);
        Assert.Contains("variable-data record", history[0].Notes.Single(), StringComparison.Ordinal);
        // A listing is a listing: it names the files without carrying their bytes.
        Assert.All(history[0].Files, file => Assert.True(file.ByteCount > 0));
    }

    [SqlServerFact]
    public async Task A_stored_file_is_not_readable_through_another_asset()
    {
        var figureCode = $"TEST{Random.Shared.Next(100000, 999999)}";
        var mine = await NewAssetAsync(figureCode);
        var other = await NewAssetAsync($"TEST{Random.Shared.Next(100000, 999999)}");
        await sql.Assets.AddAsync(mine);
        await sql.Assets.AddAsync(other);

        var stored = await sql.ClearingFiles.AddAsync(ClearingSet(mine.Id, figureCode, "0001"));
        var fileId = stored.Files.Single().Id;

        Assert.NotNull(await sql.ClearingFiles.GetFileAsync(mine.Id, fileId));
        Assert.Null(await sql.ClearingFiles.GetFileAsync(other.Id, fileId));
    }

    [SqlServerFact]
    public async Task One_file_per_operation_within_a_generation()
    {
        var figureCode = $"TEST{Random.Shared.Next(100000, 999999)}";
        var asset = await NewAssetAsync(figureCode);
        await sql.Assets.AddAsync(asset);

        // A certificate produces at most one Registro COE; two in the same set is a bug that
        // should not reach the database.
        var duplicated = ClearingSet(asset.Id, figureCode, "0001", "0001");

        await Assert.ThrowsAsync<SqlException>(() => sql.ClearingFiles.AddAsync(duplicated));

        // The set went in with it and has to be gone too, or the history would show a
        // generation that produced nothing.
        Assert.Empty(await sql.ClearingFiles.ListAsync(asset.Id));
    }
}
