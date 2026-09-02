using System.Text.Json.Nodes;
using Coe.Clearing;
using Coe.Core.Templates;
using Xunit;

namespace Coe.Tests;

/// <summary>
/// The fixed-width builder the upload layouts are written against. Its job is to make a
/// mistranscribed position impossible to ship, so most of these are about it refusing.
/// </summary>
public sealed class FixedWidthRecordTests
{
    private static FixedWidthRecord Record() => new("test", "record");

    [Fact]
    public void Pads_text_to_the_right_and_numbers_to_the_left()
    {
        var line = Record().Text(1, 5, "AB").Number(6, 10, 42).Build(10);
        Assert.Equal("AB   00042", line);
    }

    [Fact]
    public void Writes_an_amount_with_the_decimal_point_implied()
    {
        // 12 integer digits and 8 decimals, which is how every price in these layouts is written.
        Assert.Equal("00000000000150000000", Record().Amount(1, 20, 1.5m, 8).Build(20));
    }

    [Fact]
    public void Rounds_an_amount_to_the_precision_the_layout_registers()
    {
        // Rounded away from zero at the fourth decimal: 100.00005 registers as 100.0001.
        Assert.Equal("1000001", Record().Amount(1, 7, 100.00005m, 4).Build(7));
    }

    [Fact]
    public void Writes_a_date_as_the_manual_does()
    {
        Assert.Equal("20260828", Record().Date(1, 8, new DateOnly(2026, 8, 28)).Build(8));
    }

    [Fact]
    public void Leaves_a_field_blank_whatever_its_type_when_there_is_nothing_to_say()
    {
        Assert.Equal(new string(' ', 12), Record().Number(1, 4, null).Date(5, 12, null).Build(12));
    }

    [Fact]
    public void Refuses_a_field_that_does_not_start_where_the_layout_says()
    {
        var record = Record().Text(1, 5, "AB");

        // 7 skips position 6: a field was left out, or a position was copied wrongly.
        var error = Assert.Throws<ClearingFormatException>(() => record.Text(7, 10, "CD"));
        Assert.Contains("would start at 6", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_record_that_does_not_end_where_the_layout_says()
    {
        var error = Assert.Throws<ClearingFormatException>(() => Record().Text(1, 5, "AB").Build(10));
        Assert.Contains("came out 5", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_value_too_long_for_its_field_rather_than_truncating_it()
    {
        Assert.Throws<ClearingFormatException>(() => Record().Text(1, 4, "TOOLONG").Build(4));
        Assert.Throws<ClearingFormatException>(() => Record().Number(1, 3, 12345).Build(3));
        Assert.Throws<ClearingFormatException>(() => Record().Amount(1, 4, 999m, 2).Build(4));
    }

    [Fact]
    public void Refuses_a_negative_where_the_layout_carries_no_sign()
    {
        Assert.Throws<ClearingFormatException>(() => Record().Amount(1, 10, -1m, 2).Build(10));
    }
}

/// <summary>
/// The registration files, written from a booked instance of a real figure compiled out of
/// <c>domain/</c> — so a change to a fragment that breaks the upload shows up here.
/// </summary>
public sealed class ClearingFileTests
{
    private const string Figure = "COE001005";
    private const string Participant = "BANCOTESTE";

    private static ClearingRequest Request(Action<JsonObject>? adjust = null, string figureCode = Figure)
    {
        var template = DomainFiles.Template(figureCode);
        var values = MinimalAsset();
        adjust?.Invoke(values);
        return new ClearingRequest(template, values, Participant, "05", new DateOnly(2026, 8, 28), "MM05261234");
    }

    /// <summary>A capital-protected call spread on PETR4, with nothing optional filled in.</summary>
    private static JsonObject MinimalAsset() => new()
    {
        ["common"] = new JsonObject
        {
            ["issuerAccount"] = "12345409",
            ["commercialName"] = "COE Call Spread PETR4",
            ["externalIdentifier"] = "DESK-2026-0001",
            ["isin"] = "BRCOEXCTF001",
            ["issueDate"] = "2026-09-01",
            ["maturityDate"] = "2027-09-01",
            ["quantity"] = 1000,
            ["unitIssuePrice"] = 1000m,
            ["notional"] = 1_000_000m,
            ["modality"] = "VNP",
            ["guaranteedCapital"] = 100m
        },
        ["underlying"] = new JsonObject
        {
            ["assetClass"] = "ACOES",
            ["asset"] = "PETR4",
            ["initialValue"] = 38.5m,
            ["fixingWindow"] = "DATA_UNICA",
            ["fixingDate"] = "2027-08-30",
            ["quoteType"] = "FECHAMENTO",
            ["hasLookback"] = false,
            ["dividendProtection"] = "EMISSOR"
        },
        ["remuneration"] = new JsonObject
        {
            ["maturityRemunerator"] = "SEM_REMUNERACAO",
            ["hasCashFlow"] = false
        },
        ["terms"] = new JsonObject
        {
            ["baseApplication"] = 100m,
            ["issuerPosition"] = "COMPRADO",
            ["custodyRegime"] = "DEPOSITADO",
            ["cvmResolution8"] = true,
            ["dieReference"] = "DIE-2026-0001",
            ["earlyRedemption"] = "SEM_LIQUIDEZ"
        },
        ["payoff"] = new JsonObject
        {
            ["strike"] = 100m,
            ["participation"] = 100m
        }
    };

    [Fact]
    public void Registration_records_are_the_length_the_layout_declares()
    {
        var file = CetipRegistrationFiles.Registration(Request());

        Assert.Equal(38, file.Lines[0].Length);
        Assert.Equal(1103, file.Lines[1].Length);
        Assert.All(file.Lines.Skip(2), line => Assert.Equal(326, line.Length));
    }

    [Fact]
    public void Registration_header_carries_the_participant_and_the_operation()
    {
        var header = CetipRegistrationFiles.Registration(Request()).Lines[0];

        Assert.Equal("COE  ", header[..5]);
        Assert.Equal("0", header.Substring(5, 1));
        Assert.Equal("0001", header.Substring(6, 4));
        Assert.Equal("BANCOTESTE          ", header.Substring(10, 20));
        Assert.Equal("20260828", header.Substring(30, 8));
    }

    [Fact]
    public void Fixed_record_writes_the_registration_fields_where_the_manual_puts_them()
    {
        var fixedRecord = CetipRegistrationFiles.Registration(Request()).Lines[1];

        Assert.Equal("12345409", fixedRecord.Substring(10, 8));          // seq 04, conta emissor
        Assert.Equal("05", fixedRecord.Substring(18, 2));                // seq 05, tipo COE
        Assert.StartsWith("COE Call Spread PETR4", fixedRecord[20..120]); // seq 06
        Assert.Equal("20260901", fixedRecord.Substring(120, 8));         // seq 07
        Assert.Equal("20270901", fixedRecord.Substring(128, 8));         // seq 08
        Assert.Equal("000000001000", fixedRecord.Substring(136, 12));    // seq 09
        Assert.Equal("01", fixedRecord.Substring(206, 2));               // seq 14, VNP
        Assert.Equal("1000000", fixedRecord.Substring(208, 7));          // seq 15, 100.0000%
        Assert.Equal("S", fixedRecord.Substring(222, 1));                // seq 17, CVM 8
        Assert.Equal("C", fixedRecord.Substring(225, 1));                // seq 19, comprado
        Assert.Equal("PETR4     ", fixedRecord.Substring(256, 10));      // seq 21
        Assert.Equal("D", fixedRecord.Substring(726, 1));                // seq 45, depositado
    }

    [Fact]
    public void Amounts_are_written_with_the_decimal_point_implied()
    {
        var fixedRecord = CetipRegistrationFiles.Registration(Request()).Lines[1];

        // seq 10: 12 integer digits and 8 decimals, for a unit price of 1,000.
        Assert.Equal("00000000100000000000", fixedRecord.Substring(148, 20));
        // seq 11: 16 and 2, for a financial value of 1,000,000.
        Assert.Equal("000000000100000000", fixedRecord.Substring(168, 18));
    }

    [Fact]
    public void Variable_records_carry_one_of_B3s_own_field_codes_each()
    {
        var file = CetipRegistrationFiles.Registration(Request());
        var variables = file.Lines.Skip(2).ToList();

        Assert.NotEmpty(variables);
        Assert.All(variables, line =>
        {
            Assert.Equal("2", line.Substring(5, 1));
            var code = line.Substring(18, 8).Trim();
            Assert.StartsWith("C", code, StringComparison.Ordinal);
            Assert.NotNull(DomainFiles.Reference.DerivativeField(code));
        });

        // seq 31 of the fixed record counts them.
        var declared = int.Parse(file.Lines[1].Substring(623, 3));
        Assert.Equal(variables.Count, declared);
    }

    [Fact]
    public void A_basket_registers_its_components_on_their_own_file()
    {
        var request = Request(values =>
        {
            var underlying = (JsonObject)values["underlying"]!;
            underlying["assetClass"] = "CESTA";
            underlying["asset"] = null;
            underlying["initialValue"] = null;
            underlying["basketType"] = "STANDARD";
            underlying["basketParityCurrency"] = "BRL";
            values["basket"] = new JsonArray
            {
                new JsonObject
                {
                    ["component"] = "PETR4", ["weight"] = 60m, ["componentInitialValue"] = 38.5m,
                    ["componentQuoteType"] = "FECHAMENTO", ["componentFixingDate"] = "2026-09-01"
                },
                new JsonObject
                {
                    ["component"] = "VALE3", ["weight"] = 40m, ["componentInitialValue"] = 62.1m,
                    ["componentQuoteType"] = "FECHAMENTO", ["componentFixingDate"] = "2026-09-01"
                }
            };
        });

        var set = ClearingFileGenerator.ForRegistration(request);
        var basket = set.Find("CEST");

        Assert.NotNull(basket);
        Assert.Equal(2, basket.VariableRecordCount);
        Assert.Equal(38, basket.Lines[0].Length);
        Assert.Equal(77, basket.Lines[1].Length);
        Assert.All(basket.Lines.Skip(2), line => Assert.Equal(58, line.Length));

        Assert.Equal("03", basket.Lines[1].Substring(49, 2));   // seq 07, standard
        Assert.Equal("02", basket.Lines[1].Substring(51, 2));   // seq 08, two assets
        Assert.Equal("001", basket.Lines[1].Substring(53, 3));  // seq 09, BRL
        Assert.Equal("PETR4     ", basket.Lines[2][10..20]);
        Assert.Equal("01", basket.Lines[2].Substring(20, 2));   // fechamento
        Assert.Equal("00600000", basket.Lines[2].Substring(50, 8)); // 60.0000%

        // A basket registers no initial value or parity on the registration itself.
        var registration = set.Find("0001")!.Lines[1];
        Assert.Equal("CESTA     ", registration.Substring(256, 10));
        Assert.Equal(new string(' ', 20), registration.Substring(266, 20));
    }

    [Fact]
    public void A_worst_of_basket_registers_no_weights()
    {
        var request = Request(values =>
        {
            var underlying = (JsonObject)values["underlying"]!;
            underlying["assetClass"] = "CESTA";
            underlying["asset"] = null;
            underlying["basketType"] = "WORST_OF";
            underlying["basketParityCurrency"] = "BRL";
            values["basket"] = new JsonArray
            {
                new JsonObject
                {
                    ["component"] = "PETR4", ["componentInitialValue"] = 38.5m,
                    ["componentQuoteType"] = "FECHAMENTO", ["componentFixingDate"] = "2026-09-01"
                }
            };
        });

        var basket = CetipRegistrationFiles.Basket(request);
        Assert.Equal("        ", basket.Lines[2].Substring(50, 8));
    }

    [Fact]
    public void A_cash_flow_certificate_gets_its_schedule_file()
    {
        var request = Request(values =>
        {
            var remuneration = (JsonObject)values["remuneration"]!;
            remuneration["hasCashFlow"] = true;
            remuneration["flowRemunerator"] = "PRE";
            remuneration["flowBasis"] = "252_EXP";
            remuneration["couponBarrierCondition"] = "ACIMA";
            remuneration["flowCouponMemory"] = true;
            values["cashflows"] = new JsonArray
            {
                new JsonObject
                {
                    ["paymentDate"] = "2027-03-01", ["flowSpread"] = 6m, ["amountPercent"] = 0m,
                    ["couponBarrier"] = 100m, ["fixingDate"] = "2027-02-25"
                },
                new JsonObject
                {
                    ["paymentDate"] = "2027-09-01", ["flowSpread"] = 6m, ["amountPercent"] = 100m,
                    ["couponBarrier"] = 100m, ["fixingDate"] = "2027-08-27"
                }
            };
        });

        var flow = ClearingFileGenerator.ForRegistration(request).Find("FLUX");

        Assert.NotNull(flow);
        Assert.Equal(2, flow.VariableRecordCount);
        Assert.Equal(61, flow.Lines[1].Length);
        Assert.All(flow.Lines.Skip(2), line => Assert.Equal(101, line.Length));

        Assert.Equal("MM05261234 ", flow.Lines[1][18..29]);        // seq 05, código IF
        Assert.Equal("01", flow.Lines[1].Substring(49, 2));        // seq 07, acima
        Assert.Equal("002", flow.Lines[1].Substring(53, 3));       // seq 09, two events
        Assert.Equal("S", flow.Lines[1].Substring(56, 1));         // seq 10, memory
        Assert.Equal("01", flow.Lines[1].Substring(57, 2));        // seq 11, pré
        Assert.Equal("01", flow.Lines[1].Substring(59, 2));        // seq 12, 252 exp

        Assert.Equal("20270301", flow.Lines[2][10..18]);           // first event
        Assert.Equal("        ", flow.Lines[2].Substring(18, 8));  // no floating rate on a fixed flow
        Assert.Equal("000060000", flow.Lines[2].Substring(26, 9)); // spread 6.0000%
        Assert.Equal("20270225", flow.Lines[2].Substring(59, 8));  // fixing date
    }

    [Fact]
    public void An_explicit_fixing_schedule_gets_its_own_file()
    {
        var request = Request(values =>
        {
            var underlying = (JsonObject)values["underlying"]!;
            underlying["fixingWindow"] = "MAIS_DATAS";
            underlying["fixingDate"] = null;
            underlying["maturityFixingType"] = "MEDIA";
            values["fixingDates"] = new JsonArray
            {
                new JsonObject { ["fixingDate"] = "2027-08-27" },
                new JsonObject { ["fixingDate"] = "2027-08-30" }
            };
        });

        var dates = ClearingFileGenerator.ForRegistration(request).Find("DTFX");

        Assert.NotNull(dates);
        Assert.Equal(32, dates.Lines[1].Length);
        Assert.Equal("002", dates.Lines[1].Substring(29, 3));
        Assert.Equal(2, dates.VariableRecordCount);
        Assert.All(dates.Lines.Skip(2), line => Assert.Equal(18, line.Length));
        Assert.Equal("20270827", dates.Lines[2][10..18]);
    }

    [Fact]
    public void A_plain_certificate_produces_the_registration_alone()
    {
        var set = ClearingFileGenerator.ForRegistration(Request());

        Assert.Single(set.Files);
        Assert.Equal("0001", set.Files[0].Operation);
    }

    [Fact]
    public void Files_are_encoded_one_byte_per_character()
    {
        var file = CetipRegistrationFiles.Registration(
            Request(values => ((JsonObject)values["common"]!)["commercialName"] = "COE Ações Protegido"));

        // Every line plus its CRLF; an accented name must not shift the fields after it.
        Assert.Equal(file.Content.Length, file.ToBytes().Length);
        Assert.Equal(file.Lines.Sum(l => l.Length + 2), file.ToBytes().Length);
    }

    [Fact]
    public void A_schedule_goes_out_as_the_numbered_run_B3_registers()
    {
        // The form shows the autocall schedule as rows; B3's file has no row index, so it
        // registers "Data de Observação 1..10" and the participation used on each. This is the
        // join working end to end: two rows become four variable records under four codes.
        var request = Request(values =>
        {
            values["autocall"] = new JsonObject { ["hasAutocall"] = true, ["triggerType"] = "FECHAMENTO" };
            values["observations"] = new JsonArray
            {
                new JsonObject
                {
                    ["observationDate"] = "2027-03-01", ["triggerLevel"] = 100m, ["participation"] = 110m
                },
                new JsonObject
                {
                    ["observationDate"] = "2027-09-01", ["triggerLevel"] = 100m, ["participation"] = 120m
                }
            };
        }, figureCode: "COE001064");

        var variables = CetipRegistrationFiles.Registration(request).Lines.Skip(2)
            .ToDictionary(line => line.Substring(18, 8).Trim(), line => line[26..].TrimEnd());

        Assert.Equal("20270301", variables["C0000128"]);
        Assert.Equal("20270901", variables["C0000129"]);
        Assert.DoesNotContain("C0000130", variables.Keys);   // only the rows that exist

        Assert.Equal("110.00000000", variables["C0000109"]);
        Assert.Equal("120.00000000", variables["C0000110"]);
    }

    [Fact]
    public void Every_catalogue_figure_can_be_written_out()
    {
        // Not a check of the values, which are the same minimal ones throughout, but of the
        // layout: no figure's template may produce a record of the wrong length or a value that
        // will not fit the field the manual gives it.
        foreach (var code in DomainFiles.Compiled.Keys)
        {
            var file = CetipRegistrationFiles.Registration(Request(figureCode: code));
            Assert.Equal(1103, file.Lines[1].Length);
        }
    }
}
