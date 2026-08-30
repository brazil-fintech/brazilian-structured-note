using System.Text.Json.Nodes;

namespace Coe.Core.Expressions;

public sealed class ExpressionException(string message) : Exception(message);

/// <summary>
/// Evaluates the portable AST. Kept deliberately total: unknown paths and incomparable
/// operands produce <c>null</c> instead of throwing, so a half-filled form never blows up
/// mid-typing. Only a malformed template (unknown operator, wrong arity) throws.
/// </summary>
public static class ExpressionEvaluator
{
    public static bool EvaluateAsBool(Expr? expr, EvaluationContext ctx) =>
        expr is null || Values.Truthy(Evaluate(expr, ctx));

    public static object? Evaluate(Expr expr, EvaluationContext ctx) => expr switch
    {
        ConstExpr c => Values.FromJson(c.V),
        FieldExpr f => ctx.Item is not null && ctx.ResolveItemPath(f.P) is { } fromItem ? fromItem : ctx.ResolvePath(f.P),
        ItemExpr i => ctx.ResolveItemPath(i.P),
        VarExpr v => ctx.ResolveVariable(v.N),
        OpExpr o => EvaluateOp(o, ctx),
        FnExpr fn => EvaluateFn(fn, ctx),
        _ => throw new ExpressionException($"Unsupported expression node '{expr.GetType().Name}'.")
    };

    private static object? EvaluateOp(OpExpr op, EvaluationContext ctx)
    {
        switch (op.O)
        {
            case Ops.And:
                foreach (var a in op.A)
                    if (!Values.Truthy(Evaluate(a, ctx))) return false;
                return true;

            case Ops.Or:
                foreach (var a in op.A)
                    if (Values.Truthy(Evaluate(a, ctx))) return true;
                return false;

            case Ops.Not:
                Arity(op, 1);
                return !Values.Truthy(Evaluate(op.A[0], ctx));

            case Ops.Eq:
                Arity(op, 2);
                return Values.Equal(Evaluate(op.A[0], ctx), Evaluate(op.A[1], ctx));

            case Ops.Neq:
                Arity(op, 2);
                return !Values.Equal(Evaluate(op.A[0], ctx), Evaluate(op.A[1], ctx));

            case Ops.Gt or Ops.Gte or Ops.Lt or Ops.Lte:
            {
                Arity(op, 2);
                var cmp = Values.Compare(Evaluate(op.A[0], ctx), Evaluate(op.A[1], ctx));
                if (cmp is null) return null;
                return op.O switch
                {
                    Ops.Gt => cmp > 0,
                    Ops.Gte => cmp >= 0,
                    Ops.Lt => cmp < 0,
                    _ => cmp <= 0
                };
            }

            case Ops.Neg:
            {
                Arity(op, 1);
                var n = Values.AsNumber(Evaluate(op.A[0], ctx));
                return n is null ? null : -n.Value;
            }

            case Ops.Add or Ops.Sub or Ops.Mul or Ops.Div or Ops.Mod:
            {
                Arity(op, 2);
                var left = Evaluate(op.A[0], ctx);
                var right = Evaluate(op.A[1], ctx);
                if (op.O == Ops.Add && (left is string || right is string))
                    return (Values.AsString(left) ?? "") + (Values.AsString(right) ?? "");

                // date +/- days, and date - date giving a day count
                if (left is DateOnly ld && op.O is Ops.Add or Ops.Sub)
                {
                    if (right is DateOnly rd && op.O == Ops.Sub) return (decimal)(ld.DayNumber - rd.DayNumber);
                    var days = Values.AsNumber(right);
                    if (days is null) return null;
                    return ld.AddDays((int)(op.O == Ops.Add ? days.Value : -days.Value));
                }

                var a = Values.AsNumber(left);
                var b = Values.AsNumber(right);
                if (a is null || b is null) return null;
                return op.O switch
                {
                    Ops.Add => a.Value + b.Value,
                    Ops.Sub => a.Value - b.Value,
                    Ops.Mul => a.Value * b.Value,
                    Ops.Div => b.Value == 0m ? null : a.Value / b.Value,
                    _ => b.Value == 0m ? null : a.Value % b.Value
                };
            }

            case Ops.In:
            {
                MinArity(op, 2);
                var needle = Evaluate(op.A[0], ctx);
                var haystack = Evaluate(op.A[1], ctx);
                if (haystack is JsonArray arr)
                    return arr.Any(n => Values.Equal(needle, Values.FromJson(n)));
                return op.A.Skip(1).Any(e => Values.Equal(needle, Evaluate(e, ctx)));
            }

            case Ops.Between:
            {
                Arity(op, 3);
                var v = Evaluate(op.A[0], ctx);
                var lo = Values.Compare(v, Evaluate(op.A[1], ctx));
                var hi = Values.Compare(v, Evaluate(op.A[2], ctx));
                if (lo is null || hi is null) return null;
                return lo >= 0 && hi <= 0;
            }

            default:
                throw new ExpressionException($"Unknown operator '{op.O}'.");
        }
    }

    private static object? EvaluateFn(FnExpr fn, EvaluationContext ctx)
    {
        switch (fn.N)
        {
            case "isNull":
                Arity(fn, 1);
                return Values.IsAbsent(Evaluate(fn.A[0], ctx));

            case "notNull":
                Arity(fn, 1);
                return !Values.IsAbsent(Evaluate(fn.A[0], ctx));

            case "coalesce":
                foreach (var a in fn.A)
                {
                    var v = Evaluate(a, ctx);
                    if (!Values.IsAbsent(v)) return v;
                }
                return null;

            case "abs":
            {
                Arity(fn, 1);
                var n = Values.AsNumber(Evaluate(fn.A[0], ctx));
                return n is null ? null : Math.Abs(n.Value);
            }

            case "num":
                Arity(fn, 1);
                return Values.AsNumber(Evaluate(fn.A[0], ctx));

            case "str":
                Arity(fn, 1);
                return Values.AsString(Evaluate(fn.A[0], ctx));

            case "min" or "max":
            {
                var numbers = fn.A.Select(a => Values.AsNumber(Evaluate(a, ctx))).ToList();
                if (numbers.Count == 0 || numbers.Any(n => n is null)) return null;
                return fn.N == "min" ? numbers.Min(n => n!.Value) : numbers.Max(n => n!.Value);
            }

            case "round":
            {
                Arity(fn, 1);
                var n = Values.AsNumber(Evaluate(fn.A[0], ctx));
                if (n is null) return null;
                var digits = fn.A.Count > 1 ? (int)(Values.AsNumber(Evaluate(fn.A[1], ctx)) ?? 0m) : 0;
                return Math.Round(n.Value, Math.Clamp(digits, 0, 28), MidpointRounding.AwayFromZero);
            }

            case "floor" or "ceil":
            {
                Arity(fn, 1);
                var n = Values.AsNumber(Evaluate(fn.A[0], ctx));
                if (n is null) return null;
                return fn.N == "floor" ? Math.Floor(n.Value) : Math.Ceiling(n.Value);
            }

            case "len":
            {
                Arity(fn, 1);
                var v = Evaluate(fn.A[0], ctx);
                return v switch
                {
                    string s => (decimal)s.Length,
                    JsonArray arr => (decimal)arr.Count,
                    null => 0m,
                    _ => null
                };
            }

            case "count":
            {
                Arity(fn, 1);
                return (decimal)Values.AsList(Evaluate(fn.A[0], ctx)).Count;
            }

            case "sum":
            {
                Arity(fn, 2);
                var total = 0m;
                foreach (var item in Values.AsList(Evaluate(fn.A[0], ctx)))
                {
                    var scoped = ctx.WithItem(item as JsonObject);
                    total += Values.AsNumber(Evaluate(fn.A[1], scoped)) ?? 0m;
                }
                return total;
            }

            case "any" or "all":
            {
                Arity(fn, 2);
                var items = Values.AsList(Evaluate(fn.A[0], ctx));
                var wantAll = fn.N == "all";
                foreach (var item in items)
                {
                    var scoped = ctx.WithItem(item as JsonObject);
                    var hit = Values.Truthy(Evaluate(fn.A[1], scoped));
                    if (wantAll && !hit) return false;
                    if (!wantAll && hit) return true;
                }
                return wantAll;
            }

            case "isDistinct":
            {
                Arity(fn, 2);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in Values.AsList(Evaluate(fn.A[0], ctx)))
                {
                    var scoped = ctx.WithItem(item as JsonObject);
                    var key = Values.AsString(Evaluate(fn.A[1], scoped)) ?? "<null>";
                    if (!seen.Add(key)) return false;
                }
                return true;
            }

            case "year" or "month" or "day":
            {
                Arity(fn, 1);
                var d = Values.AsDate(Evaluate(fn.A[0], ctx));
                if (d is null) return null;
                return fn.N switch
                {
                    "year" => (decimal)d.Value.Year,
                    "month" => (decimal)d.Value.Month,
                    _ => (decimal)d.Value.Day
                };
            }

            case "daysBetween":
            {
                Arity(fn, 2);
                var from = Values.AsDate(Evaluate(fn.A[0], ctx));
                var to = Values.AsDate(Evaluate(fn.A[1], ctx));
                return from is null || to is null ? null : (decimal)(to.Value.DayNumber - from.Value.DayNumber);
            }

            case "addDays":
            {
                Arity(fn, 2);
                var d = Values.AsDate(Evaluate(fn.A[0], ctx));
                var n = Values.AsNumber(Evaluate(fn.A[1], ctx));
                return d is null || n is null ? null : d.Value.AddDays((int)n.Value);
            }

            case "today":
                return ctx.ResolveVariable("today");

            case "contains":
            {
                Arity(fn, 2);
                var haystack = Evaluate(fn.A[0], ctx);
                var needle = Evaluate(fn.A[1], ctx);
                if (haystack is JsonArray arr) return arr.Any(n => Values.Equal(needle, Values.FromJson(n)));
                var hs = Values.AsString(haystack);
                var ns = Values.AsString(needle);
                return hs is null || ns is null ? null : hs.Contains(ns, StringComparison.Ordinal);
            }

            case "upper" or "lower":
            {
                Arity(fn, 1);
                var s = Values.AsString(Evaluate(fn.A[0], ctx));
                return s is null ? null : fn.N == "upper" ? s.ToUpperInvariant() : s.ToLowerInvariant();
            }

            default:
                throw new ExpressionException($"Unknown function '{fn.N}'.");
        }
    }

    private static void Arity(OpExpr op, int expected)
    {
        if (op.A.Count != expected)
            throw new ExpressionException($"Operator '{op.O}' expects {expected} argument(s), got {op.A.Count}.");
    }

    private static void MinArity(OpExpr op, int expected)
    {
        if (op.A.Count < expected)
            throw new ExpressionException($"Operator '{op.O}' expects at least {expected} argument(s), got {op.A.Count}.");
    }

    private static void Arity(FnExpr fn, int expected)
    {
        if (fn.A.Count < expected)
            throw new ExpressionException($"Function '{fn.N}' expects at least {expected} argument(s), got {fn.A.Count}.");
    }
}
