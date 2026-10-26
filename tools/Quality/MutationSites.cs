using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json.Serialization;

namespace RevitThyme.Quality;

public sealed record MutationSite(string Path, string UnitName, int Line, int Start, int Length, string Original, string Replacement,
    [property: JsonIgnore] string Source)
{
    public string Id => $"{Path}:{Start}:{Original}->{Replacement}";
    public string Apply() => Source[..Start] + Replacement + Source[(Start + Length)..];
}

public static class MutationSites
{
    private static readonly IReadOnlyDictionary<string, string> Operators = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["+"] = "-", ["-"] = "+", ["*"] = "/", ["/"] = "*",
        [">"] = ">=", [">="] = ">", ["<"] = "<=", ["<="] = "<",
        ["=="] = "!=", ["!="] = "==", ["&&"] = "||", ["||"] = "&&",
        ["and"] = "or", ["or"] = "and", ["!"] = "", ["not"] = ""
    };

    public static IReadOnlyList<MutationSite> Find(string source, string path)
    {
        var sites = new List<MutationSite>();
        foreach (var unit in SourceAnalysis.Parse(source, path))
            foreach (var node in SourceAnalysis.OwnedNodes(unit))
            {
                var token = Operator(node);
                if (Operators.TryGetValue(token.Text, out var replacement))
                    Add(token.SpanStart, token.Span.Length, token.Text, replacement);
                if (node is LiteralExpressionSyntax literal)
                {
                    if (LiteralReplacement(literal) is { } value)
                        Add(literal.SpanStart, literal.Span.Length, literal.ToString(), value);
                }
                void Add(int start, int length, string original, string replacement) => sites.Add(new(path, unit.Name,
                    node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, start, length, original, replacement, source));
            }
        return sites;
    }

    private static SyntaxToken Operator(SyntaxNode node) => node switch
    {
        BinaryExpressionSyntax binary => binary.OperatorToken,
        RelationalPatternSyntax relational => relational.OperatorToken,
        BinaryPatternSyntax pattern => pattern.OperatorToken,
        UnaryPatternSyntax pattern => pattern.OperatorToken,
        PrefixUnaryExpressionSyntax unary when unary.IsKind(SyntaxKind.LogicalNotExpression) => unary.OperatorToken,
        _ => default
    };

    private static string? LiteralReplacement(LiteralExpressionSyntax literal) => literal.Kind() switch
    {
        SyntaxKind.TrueLiteralExpression => "false",
        SyntaxKind.FalseLiteralExpression => "true",
        SyntaxKind.NumericLiteralExpression when literal.Token.Value is int n && n is 0 or 1 => n == 0 ? "1" : "0",
        _ => null
    };
}
