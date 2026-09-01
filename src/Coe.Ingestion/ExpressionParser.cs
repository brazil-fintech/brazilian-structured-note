using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Coe.Core.Expressions;

namespace Coe.Ingestion;

public sealed class ExpressionParseException(string message, int position)
    : Exception($"{message} (at position {position})")
{
    public int Position { get; } = position;
}

/// <summary>
/// Compiles the small infix language used in domain files into the portable AST.
///
/// Domain files stay readable (<c>cap &gt; 0 and cap &lt;= 500</c>); the browser never sees this
/// syntax and never needs a parser — it evaluates the AST the worker produced. Parsing once,
/// at ingestion, is also what turns a typo into a quarantined figure instead of a runtime
/// error in front of a user.
///
/// Grammar, loosest to tightest:
/// <code>
///   or        := and ( ('or' | '||') and )*
///   and       := not ( ('and' | '&amp;&amp;') not )*
///   not       := ('not' | '!') not | comparison
///   comparison:= additive ( ('==' | '!=' | '&gt;' | '&gt;=' | '&lt;' | '&lt;=' | 'in') additive )?
///   additive  := multiplicative ( ('+' | '-') multiplicative )*
///   multiplic.:= unary ( ('*' | '/' | '%') unary )*
///   unary     := '-' unary | primary
///   primary   := number | string | 'true' | 'false' | 'null'
///              | '[' list ']' | '(' or ')'
///              | '$' ident | '@' '.' path | ident ( '(' args ')' )? | path
/// </code>
/// </summary>
public sealed class ExpressionParser
{
    private readonly string _text;
    private int _pos;

    private ExpressionParser(string text) => _text = text;

    public static Expr Parse(string text)
    {
        var parser = new ExpressionParser(text);
        parser.SkipWhitespace();
        var expr = parser.ParseOr();
        parser.SkipWhitespace();
        if (!parser.AtEnd)
            throw new ExpressionParseException($"Unexpected '{parser.Peek()}'", parser._pos);
        return expr;
    }

    public static Expr? ParseOptional(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : Parse(text);

    private bool AtEnd => _pos >= _text.Length;
    private char Peek() => _text[_pos];

    // ----- grammar ------------------------------------------------------------------

    private Expr ParseOr()
    {
        var left = ParseAnd();
        var operands = new List<Expr> { left };
        while (Match("||") || MatchWord("or"))
            operands.Add(ParseAnd());
        return operands.Count == 1 ? left : new OpExpr(Ops.Or, operands);
    }

    private Expr ParseAnd()
    {
        var left = ParseNot();
        var operands = new List<Expr> { left };
        while (Match("&&") || MatchWord("and"))
            operands.Add(ParseNot());
        return operands.Count == 1 ? left : new OpExpr(Ops.And, operands);
    }

    private Expr ParseNot()
    {
        if (Match("!") || MatchWord("not"))
            return new OpExpr(Ops.Not, [ParseNot()]);
        return ParseComparison();
    }

    private Expr ParseComparison()
    {
        var left = ParseAdditive();

        // Longest operators first so '>=' is not read as '>'.
        if (Match("==")) return new OpExpr(Ops.Eq, [left, ParseAdditive()]);
        if (Match("!=")) return new OpExpr(Ops.Neq, [left, ParseAdditive()]);
        if (Match(">=")) return new OpExpr(Ops.Gte, [left, ParseAdditive()]);
        if (Match("<=")) return new OpExpr(Ops.Lte, [left, ParseAdditive()]);
        if (Match(">")) return new OpExpr(Ops.Gt, [left, ParseAdditive()]);
        if (Match("<")) return new OpExpr(Ops.Lt, [left, ParseAdditive()]);
        if (MatchWord("in"))
        {
            var right = ParseAdditive();
            // 'x in [a, b]' flattens so the evaluator can compare without allocating a list.
            if (right is ConstExpr { V: JsonArray arr })
            {
                var operands = new List<Expr> { left };
                operands.AddRange(arr.Select(n => (Expr)new ConstExpr(n?.DeepClone())));
                return new OpExpr(Ops.In, operands);
            }
            return new OpExpr(Ops.In, [left, right]);
        }
        return left;
    }

    private Expr ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (true)
        {
            if (Match("+")) left = new OpExpr(Ops.Add, [left, ParseMultiplicative()]);
            else if (Match("-")) left = new OpExpr(Ops.Sub, [left, ParseMultiplicative()]);
            else return left;
        }
    }

    private Expr ParseMultiplicative()
    {
        var left = ParseUnary();
        while (true)
        {
            if (Match("*")) left = new OpExpr(Ops.Mul, [left, ParseUnary()]);
            else if (Match("/")) left = new OpExpr(Ops.Div, [left, ParseUnary()]);
            else if (Match("%")) left = new OpExpr(Ops.Mod, [left, ParseUnary()]);
            else return left;
        }
    }

    private Expr ParseUnary()
    {
        if (Match("-")) return new OpExpr(Ops.Neg, [ParseUnary()]);
        return ParsePrimary();
    }

    private Expr ParsePrimary()
    {
        SkipWhitespace();
        if (AtEnd) throw new ExpressionParseException("Unexpected end of expression", _pos);

        var c = Peek();

        if (c == '(')
        {
            Expect('(');
            var inner = ParseOr();
            Expect(')');
            return inner;
        }

        if (c == '[')
        {
            Expect('[');
            var items = new JsonArray();
            SkipWhitespace();
            if (!AtEnd && Peek() == ']') { Expect(']'); return new ConstExpr(items); }
            while (true)
            {
                var element = ParseOr();
                if (element is not ConstExpr constant)
                    throw new ExpressionParseException("List literals accept constants only", _pos);
                items.Add(constant.V?.DeepClone());
                SkipWhitespace();
                if (Match(",")) continue;
                Expect(']');
                break;
            }
            return new ConstExpr(items);
        }

        if (c is '\'' or '"') return new ConstExpr(JsonValue.Create(ReadQuoted(c)));

        if (char.IsDigit(c)) return new ConstExpr(JsonValue.Create(ReadNumber()));

        if (c == '$')
        {
            _pos++;
            return new VarExpr(ReadIdentifier());
        }

        if (c == '@')
        {
            _pos++;
            if (!Match(".")) throw new ExpressionParseException("Expected '.' after '@'", _pos);
            return new ItemExpr(ReadPath());
        }

        if (char.IsLetter(c) || c == '_')
        {
            var start = _pos;
            var path = ReadPath();

            switch (path)
            {
                case "true": return new ConstExpr(JsonValue.Create(true));
                case "false": return new ConstExpr(JsonValue.Create(false));
                case "null": return new ConstExpr(null);
            }

            SkipWhitespace();
            if (!AtEnd && Peek() == '(')
            {
                if (path.Contains('.', StringComparison.Ordinal))
                    throw new ExpressionParseException($"'{path}' is not a function name", start);
                var args = ReadArguments();
                return BuildCall(path, args, start);
            }

            return new FieldExpr(path);
        }

        throw new ExpressionParseException($"Unexpected character '{c}'", _pos);
    }

    private static Expr BuildCall(string name, List<Expr> args, int position)
    {
        // 'between' reads better as a call but is an operator in the AST.
        if (name == "between")
        {
            if (args.Count != 3) throw new ExpressionParseException("between(value, low, high) takes 3 arguments", position);
            return new OpExpr(Ops.Between, args);
        }

        if (!Functions.All.Contains(name))
            throw new ExpressionParseException($"Unknown function '{name}'", position);

        return new FnExpr(name, args);
    }

    private List<Expr> ReadArguments()
    {
        Expect('(');
        var args = new List<Expr>();
        SkipWhitespace();
        if (!AtEnd && Peek() == ')') { Expect(')'); return args; }
        while (true)
        {
            args.Add(ParseOr());
            SkipWhitespace();
            if (Match(",")) continue;
            Expect(')');
            return args;
        }
    }

    // ----- lexing -------------------------------------------------------------------

    private void SkipWhitespace()
    {
        while (!AtEnd && char.IsWhiteSpace(Peek())) _pos++;
    }

    private bool Match(string token)
    {
        SkipWhitespace();
        if (_pos + token.Length > _text.Length) return false;
        if (string.CompareOrdinal(_text, _pos, token, 0, token.Length) != 0) return false;
        _pos += token.Length;
        return true;
    }

    /// <summary>Matches a keyword only when it is not the prefix of a longer identifier.</summary>
    private bool MatchWord(string word)
    {
        SkipWhitespace();
        if (_pos + word.Length > _text.Length) return false;
        if (string.CompareOrdinal(_text, _pos, word, 0, word.Length) != 0) return false;
        var after = _pos + word.Length;
        if (after < _text.Length && (char.IsLetterOrDigit(_text[after]) || _text[after] is '_' or '.')) return false;
        _pos = after;
        return true;
    }

    private void Expect(char c)
    {
        SkipWhitespace();
        if (AtEnd || Peek() != c) throw new ExpressionParseException($"Expected '{c}'", _pos);
        _pos++;
    }

    private string ReadIdentifier()
    {
        SkipWhitespace();
        var start = _pos;
        while (!AtEnd && (char.IsLetterOrDigit(Peek()) || Peek() == '_')) _pos++;
        if (start == _pos) throw new ExpressionParseException("Expected an identifier", _pos);
        return _text[start.._pos];
    }

    /// <summary>Reads a dotted path such as <c>payoff.cap</c> or a bare name such as <c>cap</c>.</summary>
    private string ReadPath()
    {
        var sb = new StringBuilder(ReadIdentifier());
        while (!AtEnd && Peek() == '.' && _pos + 1 < _text.Length && (char.IsLetter(_text[_pos + 1]) || _text[_pos + 1] == '_'))
        {
            _pos++;
            sb.Append('.').Append(ReadIdentifier());
        }
        return sb.ToString();
    }

    private decimal ReadNumber()
    {
        var start = _pos;
        while (!AtEnd && (char.IsDigit(Peek()) || Peek() == '.'))
        {
            // A '.' only belongs to the number when a digit follows it.
            if (Peek() == '.' && (_pos + 1 >= _text.Length || !char.IsDigit(_text[_pos + 1]))) break;
            _pos++;
        }
        var slice = _text[start.._pos];
        if (!decimal.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new ExpressionParseException($"Invalid number '{slice}'", start);
        return value;
    }

    private string ReadQuoted(char quote)
    {
        _pos++; // opening quote
        var sb = new StringBuilder();
        while (true)
        {
            if (AtEnd) throw new ExpressionParseException("Unterminated string literal", _pos);
            var c = _text[_pos++];
            if (c == '\\' && !AtEnd)
            {
                var escaped = _text[_pos++];
                sb.Append(escaped switch { 'n' => '\n', 't' => '\t', _ => escaped });
                continue;
            }
            if (c == quote) return sb.ToString();
            sb.Append(c);
        }
    }
}
