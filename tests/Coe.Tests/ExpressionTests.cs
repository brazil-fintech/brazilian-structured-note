using System.Text.Json.Nodes;
using Coe.Core.Expressions;
using Coe.Ingestion;
using Xunit;

namespace Coe.Tests;

public class ExpressionTests
{
    private static object? Eval(string source, string json = "{}")
    {
        var expr = ExpressionParser.Parse(source);
        var ctx = new EvaluationContext(JsonNode.Parse(json)!.AsObject());
        return ExpressionEvaluator.Evaluate(expr, ctx);
    }

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("-3 + 5", 2)]
    [InlineData("7 % 4", 3)]
    public void Arithmetic_follows_precedence(string source, double expected) =>
        Assert.Equal((decimal)expected, Eval(source));

    [Theory]
    [InlineData("1 < 2 and 2 < 3", true)]
    [InlineData("1 > 2 or 2 < 3", true)]
    [InlineData("not (1 == 1)", false)]
    [InlineData("'VNP' in ['VNP', 'VNR']", true)]
    [InlineData("'VNX' in ['VNP', 'VNR']", false)]
    [InlineData("between(5, 1, 10)", true)]
    [InlineData("between(50, 1, 10)", false)]
    public void Logic_and_membership(string source, bool expected) =>
        Assert.Equal(expected, Eval(source));

    [Fact]
    public void Dates_compare_and_subtract()
    {
        const string json = """{"common":{"issueDate":"2026-01-05","maturityDate":"2028-01-05"}}""";
        Assert.Equal(true, Eval("common.maturityDate > common.issueDate", json));
        Assert.Equal(730m, Eval("daysBetween(common.issueDate, common.maturityDate)", json));
        Assert.Equal(new DateOnly(2026, 1, 12), Eval("addDays(common.issueDate, 7)", json));
    }

    [Fact]
    public void Missing_values_make_comparisons_undecided()
    {
        // A half-filled form must not produce a verdict — null means "cannot tell yet".
        Assert.Null(Eval("payoff.cap > 0", """{"payoff":{}}"""));
        Assert.Equal(true, Eval("isNull(payoff.cap)", """{"payoff":{}}"""));
        Assert.Equal(true, Eval("isNull(payoff.cap)", """{"payoff":{"cap":""}}"""));
    }

    [Fact]
    public void Division_by_zero_is_null_not_an_exception() =>
        Assert.Null(Eval("1 / 0"));

    [Fact]
    public void Collection_functions_scope_to_the_item()
    {
        const string json = """
        {"basket":[{"component":"PETR4","weight":60},{"component":"VALE3","weight":40}]}
        """;
        Assert.Equal(100m, Eval("sum(basket, @.weight)", json));
        Assert.Equal(2m, Eval("count(basket)", json));
        Assert.Equal(true, Eval("all(basket, @.weight > 0)", json));
        Assert.Equal(false, Eval("any(basket, @.weight > 90)", json));
        Assert.Equal(true, Eval("isDistinct(basket, @.component)", json));
    }

    [Fact]
    public void Duplicate_rows_are_detected()
    {
        const string json = """
        {"cashflows":[{"paymentDate":"2027-01-05"},{"paymentDate":"2027-01-05"}]}
        """;
        Assert.Equal(false, Eval("isDistinct(cashflows, @.paymentDate)", json));
    }

    [Fact]
    public void Unknown_function_is_rejected_at_parse_time() =>
        Assert.Throws<ExpressionParseException>(() => ExpressionParser.Parse("nosuchfunction(1)"));

    [Fact]
    public void Unbalanced_parentheses_are_rejected() =>
        Assert.Throws<ExpressionParseException>(() => ExpressionParser.Parse("(1 + 2"));

    [Fact]
    public void Keywords_are_not_confused_with_identifiers()
    {
        // 'organic' starts with 'or' and 'android' with 'and'; neither is an operator.
        var expr = ExpressionParser.Parse("organic");
        Assert.Equal(new FieldExpr("organic"), expr);
    }
}
