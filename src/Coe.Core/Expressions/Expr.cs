using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Coe.Core.Expressions;

/// <summary>
/// Node of the portable expression AST used by figure templates.
///
/// Rules and conditions are authored as short infix strings in the domain files
/// (<c>cap &gt; 0 &amp;&amp; cap &lt;= 5</c>); the ingestion worker parses them once and stores the
/// resulting AST inside the template. Both the API (<see cref="ExpressionEvaluator"/>) and
/// the React client (<c>web/src/engine/evaluate.ts</c>) evaluate the AST, so a rule can
/// never mean two different things on the two sides.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "k")]
[JsonDerivedType(typeof(ConstExpr), "const")]
[JsonDerivedType(typeof(FieldExpr), "field")]
[JsonDerivedType(typeof(ItemExpr), "item")]
[JsonDerivedType(typeof(VarExpr), "var")]
[JsonDerivedType(typeof(OpExpr), "op")]
[JsonDerivedType(typeof(FnExpr), "fn")]
public abstract record Expr
{
    /// <summary>Field paths this expression reads, used to build rule dependency sets.</summary>
    public IReadOnlyCollection<string> Dependencies()
    {
        var acc = new SortedSet<string>(StringComparer.Ordinal);
        Collect(this, acc);
        return acc;
    }

    private static void Collect(Expr e, SortedSet<string> acc)
    {
        switch (e)
        {
            case FieldExpr f:
                acc.Add(f.P);
                break;
            case OpExpr o:
                foreach (var a in o.A) Collect(a, acc);
                break;
            case FnExpr fn:
                foreach (var a in fn.A) Collect(a, acc);
                break;
        }
    }
}

/// <summary>A literal. <c>null</c>, boolean, number, string or ISO date string.</summary>
public sealed record ConstExpr(JsonNode? V) : Expr;

/// <summary>Reads a value from the instance by absolute path, e.g. <c>payoff.cap</c>.</summary>
public sealed record FieldExpr(string P) : Expr;

/// <summary>
/// Reads a value from the current item of a repeating section — the <c>@.field</c> form,
/// only meaningful inside <c>any/all/sum/count/isDistinct</c> or a repeating-section rule.
/// </summary>
public sealed record ItemExpr(string P) : Expr;

/// <summary>Reads a context variable supplied by the host (<c>$referenceDate</c>, <c>$today</c>, …).</summary>
public sealed record VarExpr(string N) : Expr;

/// <summary>An operator application. See <see cref="Ops"/> for the closed set.</summary>
public sealed record OpExpr(string O, IReadOnlyList<Expr> A) : Expr;

/// <summary>A function call. See <see cref="Functions"/> for the closed set.</summary>
public sealed record FnExpr(string N, IReadOnlyList<Expr> A) : Expr;

/// <summary>The operator set shared by the C# and TypeScript evaluators.</summary>
public static class Ops
{
    public const string And = "and";
    public const string Or = "or";
    public const string Not = "not";
    public const string Eq = "eq";
    public const string Neq = "neq";
    public const string Gt = "gt";
    public const string Gte = "gte";
    public const string Lt = "lt";
    public const string Lte = "lte";
    public const string Add = "add";
    public const string Sub = "sub";
    public const string Mul = "mul";
    public const string Div = "div";
    public const string Mod = "mod";
    public const string Neg = "neg";
    public const string In = "in";
    public const string Between = "between";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        And, Or, Not, Eq, Neq, Gt, Gte, Lt, Lte, Add, Sub, Mul, Div, Mod, Neg, In, Between
    };
}

/// <summary>The function set shared by the C# and TypeScript evaluators.</summary>
public static class Functions
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        // null handling
        "isNull", "notNull", "coalesce",
        // numeric
        "abs", "min", "max", "round", "floor", "ceil", "num",
        // collections — the second argument is evaluated once per item with @ bound to it
        "len", "count", "sum", "any", "all", "isDistinct",
        // dates
        "year", "month", "day", "daysBetween", "addDays", "today",
        // text
        "contains", "upper", "lower", "str"
    };

    /// <summary>Functions whose argument at index 1 is an item-scoped predicate/projection.</summary>
    public static readonly IReadOnlySet<string> ItemScoped = new HashSet<string>(StringComparer.Ordinal)
    {
        "sum", "any", "all", "isDistinct"
    };
}
