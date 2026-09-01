using System.Text.Json.Nodes;
using Coe.Core.Templates;
using Coe.Core.Validation;
using Xunit;

namespace Coe.Tests;

public class ValidationEngineTests
{
    private static readonly ValidationEngine Engine = new();

    private static JsonObject CallSpread(
        string issue = "2026-09-01",
        string maturity = "2028-09-01",
        string modality = "VNP",
        decimal guaranteedCapital = 100,
        decimal strike = 100,
        decimal participation = 100,
        decimal? cap = 25)
    {
        var values = new JsonObject
        {
            ["common"] = new JsonObject
            {
                ["issuerAccount"] = "40001",
                ["commercialName"] = "COE Call Spread IBOV 2 anos",
                ["issueDate"] = issue,
                ["maturityDate"] = maturity,
                ["quantity"] = 1000,
                ["unitIssuePrice"] = 1000m,
                ["modality"] = modality,
                ["guaranteedCapital"] = guaranteedCapital
            },
            ["underlying"] = new JsonObject
            {
                ["assetClass"] = "INDICES",
                ["asset"] = "IBOV",
                ["initialValue"] = 132000m,
                ["fixingWindow"] = "DATA_UNICA",
                ["quoteType"] = "FECHAMENTO",
                ["hasLookback"] = false
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
                ["cvmResolution8"] = false,
                ["earlyRedemption"] = "SEM_LIQUIDEZ",
                ["issuerCallClause"] = false
            },
            ["payoff"] = new JsonObject
            {
                ["strike"] = strike,
                ["participation"] = participation
            }
        };

        if (cap is not null) values["payoff"]!["cap"] = cap;
        return values;
    }

    private static ValidationResult Submit(JsonObject values, string figureCode = "COE001005")
    {
        var template = DomainFiles.Template(figureCode);
        ComputedFields.Apply(template, values);
        return Engine.Validate(template, values, ValidationScope.Submit);
    }

    private static IEnumerable<string> ErrorIds(ValidationResult r) =>
        r.Messages.Where(m => m.Severity == RuleSeverity.Error).Select(m => m.RuleId ?? m.Path);

    [Fact]
    public void A_well_formed_call_spread_passes()
    {
        var result = Submit(CallSpread());
        Assert.True(result.IsValid, string.Join(" | ", ErrorIds(result)));
    }

    [Fact]
    public void Maturity_before_issue_is_rejected()
    {
        var result = Submit(CallSpread(issue: "2028-09-01", maturity: "2026-09-01"));
        Assert.Contains("common.maturity-after-issue", ErrorIds(result));
    }

    [Fact]
    public void A_capital_guaranteed_note_needs_at_least_full_protection()
    {
        var result = Submit(CallSpread(modality: "VNP", guaranteedCapital: 90));
        Assert.Contains("common.vnp-guaranteed-capital", ErrorIds(result));
    }

    [Fact]
    public void A_cap_of_zero_is_not_a_spread()
    {
        var result = Submit(CallSpread(cap: 0));
        Assert.Contains("callspread.cap-positive", ErrorIds(result));
    }

    [Fact]
    public void A_missing_required_attribute_is_reported_on_submit()
    {
        var result = Submit(CallSpread(cap: null));
        Assert.Contains(result.Messages, m => m.Path == "payoff.cap" && m.Origin == ValidationOrigin.Field);
    }

    [Fact]
    public void A_wide_cap_only_warns()
    {
        var result = Submit(CallSpread(cap: 300));
        Assert.True(result.IsValid);
        Assert.Contains(result.Messages, m => m.RuleId == "callspread.max-redemption" && m.Severity == RuleSeverity.Warning);
    }

    [Fact]
    public void The_message_lands_on_the_attribute_it_is_about()
    {
        var result = Submit(CallSpread(cap: 0));
        var message = result.Messages.Single(m => m.RuleId == "callspread.cap-positive");
        Assert.Equal("payoff.cap", message.Path);
    }

    [Fact]
    public void Computed_attributes_are_recomputed_from_their_inputs()
    {
        var values = CallSpread();
        values["common"]!["notional"] = 1m; // a client that lied, or is simply stale
        Submit(values);
        Assert.Equal(1_000_000m, values["common"]!["notional"]!.GetValue<decimal>());
    }

    [Fact]
    public void Hidden_attributes_are_not_required()
    {
        // A single-date fixing hides the window dates, so they must not be demanded.
        var result = Submit(CallSpread());
        Assert.DoesNotContain(result.Messages, m => m.Path == "underlying.fixingWindowStart");
    }

    [Fact]
    public void Conditional_requirements_apply_once_their_condition_holds()
    {
        var values = CallSpread();
        values["underlying"]!["fixingWindow"] = "JANELA";
        var result = Submit(values);

        Assert.Contains(result.Messages, m => m.Path == "underlying.fixingWindowStart" && m.Severity == RuleSeverity.Error);
        Assert.Contains(result.Messages, m => m.Path == "underlying.maturityFixingType" && m.Severity == RuleSeverity.Error);
    }

    [Fact]
    public void Field_scope_only_speaks_about_what_changed()
    {
        var template = DomainFiles.Template("COE001005");
        var values = CallSpread(cap: 0);
        values["common"]!["commercialName"] = null;

        var result = Engine.Validate(template, values, ValidationScope.Field, ["payoff.cap"]);

        Assert.Contains(result.Messages, m => m.RuleId == "callspread.cap-positive");
        Assert.DoesNotContain(result.Messages, m => m.Path == "common.commercialName");
    }

    [Fact]
    public void Field_scope_also_re_runs_the_rules_that_read_the_changed_attribute()
    {
        var template = DomainFiles.Template("COE001005");
        var values = CallSpread(modality: "VNP", guaranteedCapital: 90);

        // The user touched the modality; the rule lives on guaranteedCapital but reads both.
        var result = Engine.Validate(template, values, ValidationScope.Field, ["common.modality"]);

        Assert.Contains("common.vnp-guaranteed-capital", ErrorIds(result));
    }

    [Fact]
    public void Server_only_rules_are_skipped_when_no_check_is_registered()
    {
        // The engine without a registry must not invent a verdict for a check it cannot run.
        var result = Submit(CallSpread(issue: "2026-09-05")); // a Saturday
        Assert.DoesNotContain("common.issue-date-business-day", ErrorIds(result));
    }

    [Fact]
    public void Validation_behaves_identically_through_a_stored_template()
    {
        // The worker writes the template as JSON into MSSQL and the API reads it back. The
        // expression AST has to survive that round trip intact — if polymorphic deserialization
        // dropped a node, every rule would quietly stop firing and nothing else would notice.
        var direct = DomainFiles.Template("COE001005");
        var stored = TemplateJson.Deserialize(TemplateJson.Serialize(direct));

        var broken = CallSpread(cap: 0);
        ComputedFields.Apply(stored, broken);
        var result = Engine.Validate(stored, broken, ValidationScope.Submit);

        Assert.Contains("callspread.cap-positive", ErrorIds(result));
        Assert.Equal("payoff.cap", result.Messages.Single(m => m.RuleId == "callspread.cap-positive").Path);

        // Conditional visibility is an expression too, so it has to survive the trip as well.
        var windowed = CallSpread();
        windowed["underlying"]!["fixingWindow"] = "JANELA";
        ComputedFields.Apply(stored, windowed);
        Assert.Contains(
            Engine.Validate(stored, windowed, ValidationScope.Submit).Messages,
            m => m.Path == "underlying.fixingWindowStart");

        var clean = CallSpread();
        ComputedFields.Apply(stored, clean);
        var cleanResult = Engine.Validate(stored, clean, ValidationScope.Submit);
        Assert.True(cleanResult.IsValid, string.Join(" | ", ErrorIds(cleanResult)));
    }

    [Fact]
    public void Rule_execution_serializes_as_a_single_token_the_client_understands()
    {
        // RuleExecution is a [Flags] enum; if Both serialized as "client, server" the
        // TypeScript union ('client' | 'server' | 'both') would not match and the browser
        // would misjudge which rules it may run.
        var json = TemplateJson.Serialize(DomainFiles.Template("COE001005"));

        Assert.Contains("\"execution\":\"both\"", json);
        Assert.Contains("\"execution\":\"server\"", json);
        Assert.DoesNotContain("client, server", json);
        Assert.DoesNotContain("Client", json);
    }

    // ----- repeating sections -------------------------------------------------------

    private static JsonObject WithCashFlows(params string[] dates)
    {
        var values = CallSpread();
        values["remuneration"]!["hasCashFlow"] = true;
        var rows = new JsonArray();
        foreach (var d in dates)
            rows.Add(new JsonObject
            {
                ["paymentDate"] = d,
                ["flowRemunerator"] = "PRE",
                ["flowRate"] = 5m,
                ["amountPercent"] = 5m
            });
        values["cashflows"] = rows;
        return values;
    }

    [Fact]
    public void A_valid_cash_flow_schedule_passes()
    {
        var result = Submit(WithCashFlows("2027-03-01", "2027-09-01", "2028-03-01"));
        Assert.True(result.IsValid, string.Join(" | ", ErrorIds(result)));
    }

    [Fact]
    public void A_payment_outside_the_tenor_is_flagged_on_its_own_row()
    {
        var result = Submit(WithCashFlows("2027-03-01", "2030-01-01"));
        var message = result.Messages.Single(m => m.RuleId == "cashflows.payment-within-tenor");
        Assert.Equal("cashflows[1].paymentDate", message.Path);
    }

    [Fact]
    public void Repeated_payment_dates_are_rejected()
    {
        var result = Submit(WithCashFlows("2027-03-01", "2027-03-01"));
        Assert.Contains("cashflows.dates-distinct", ErrorIds(result));
    }

    [Fact]
    public void A_cash_flow_flag_without_a_schedule_is_rejected()
    {
        var values = CallSpread();
        values["remuneration"]!["hasCashFlow"] = true;
        var result = Submit(values);
        Assert.Contains("cashflows.require-schedule", ErrorIds(result));
    }

    [Fact]
    public void A_hidden_repeating_section_does_not_demand_rows()
    {
        // hasCashFlow is false, so the cash-flow tab is hidden and its minItems does not apply.
        var result = Submit(CallSpread());
        Assert.DoesNotContain(result.Messages, m => m.Path == "cashflows");
    }

    // ----- baskets ------------------------------------------------------------------

    private static JsonObject WithBasket(params (string Component, decimal Weight)[] components)
    {
        var values = CallSpread();
        values["underlying"]!["assetClass"] = "CESTA";
        values["underlying"]!["basketType"] = "STANDARD";
        values["underlying"]!["quanto"] = true;
        values["underlying"]!.AsObject().Remove("asset");
        values["underlying"]!.AsObject().Remove("initialValue");

        var rows = new JsonArray();
        foreach (var (component, weight) in components)
            rows.Add(new JsonObject { ["component"] = component, ["weight"] = weight });
        values["basket"] = rows;
        return values;
    }

    [Fact]
    public void A_weighted_basket_must_sum_to_one_hundred()
    {
        var result = Submit(WithBasket(("PETR4", 60), ("VALE3", 30)));
        Assert.Contains("basket.weights-sum-to-100", ErrorIds(result));
    }

    [Fact]
    public void A_balanced_basket_passes()
    {
        var result = Submit(WithBasket(("PETR4", 60), ("VALE3", 40)));
        Assert.True(result.IsValid, string.Join(" | ", ErrorIds(result)));
    }

    [Fact]
    public void Negative_weights_need_a_rainbow_basket()
    {
        var values = WithBasket(("PETR4", 130), ("VALE3", -30));
        Assert.Contains("basket.weights-non-negative", ErrorIds(Submit(values)));

        values["underlying"]!["basketType"] = "RAINBOW";
        Assert.DoesNotContain("basket.weights-non-negative", ErrorIds(Submit(values)));
    }

    [Fact]
    public void Selecting_a_basket_hides_the_single_underlying_fields()
    {
        var result = Submit(WithBasket(("PETR4", 60), ("VALE3", 40)));
        Assert.DoesNotContain(result.Messages, m => m.Path == "underlying.asset");
    }

    // ----- barrier figures ----------------------------------------------------------

    [Fact]
    public void A_shark_fin_barrier_must_sit_above_the_strike()
    {
        var values = CallSpread(cap: null);
        values["payoff"]!.AsObject().Remove("cap");
        values["payoff"]!["rebate"] = 3m;
        values["barriers"] = new JsonObject
        {
            ["barrierLevel"] = 90m,
            ["barrierDirection"] = "ALTA",
            ["barrierType"] = "KO",
            ["verificationPeriod"] = "EUROPEIA",
            ["fixingDate"] = "2028-08-25"
        };

        var result = Submit(values, "COE001003");
        Assert.Contains("sharkfin.barrier-above-strike", ErrorIds(result));
    }
}
