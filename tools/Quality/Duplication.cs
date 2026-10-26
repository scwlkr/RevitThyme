using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RevitThyme.Quality;

public sealed record Duplicate(string First, string Second, double Similarity);

public static class Duplication
{
    // The dryer score: Jaccard similarity over all normalized structural subtrees.
    // Calls and operators retain meaning; incidental identifiers and literals do not.
    private static HashSet<string> Fingerprints(CodeUnit unit)
    {
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        Normalize(unit.Body, fingerprints);
        return fingerprints;
    }

    private static string Normalize(SyntaxNode node, HashSet<string> fingerprints)
    {
        string shape;
        if (node is LiteralExpressionSyntax) shape = "literal";
        else if (node is SimpleNameSyntax name)
        {
            shape = CalledName(name) ? "call:" + name.Identifier.ValueText : "identifier";
            if (name is GenericNameSyntax generic) shape += "[" + Normalize(generic.TypeArgumentList, fingerprints) + "]";
        }
        else
        {
            var parts = node.ChildNodesAndTokens().Select(child => child.IsNode ? Normalize(child.AsNode()!, fingerprints)
                : child.AsToken().IsKind(SyntaxKind.IdentifierToken) ? "identifier" : child.AsToken().Kind().ToString());
            shape = node.Kind() + "[" + string.Join(' ', parts) + "]";
        }
        fingerprints.Add(shape);
        return shape;
    }

    private static bool CalledName(SimpleNameSyntax id) => id.Parent switch
    {
        InvocationExpressionSyntax call => call.Expression == id,
        MemberAccessExpressionSyntax member => member.Name == id && member.Parent is InvocationExpressionSyntax,
        MemberBindingExpressionSyntax binding => binding.Name == id && binding.Parent is InvocationExpressionSyntax,
        _ => ConstructedTypeName(id)
    };

    private static bool ConstructedTypeName(SimpleNameSyntax name)
    {
        SyntaxNode current = name;
        while (current.Parent is NameSyntax) current = current.Parent;
        return current.Parent is ObjectCreationExpressionSyntax creation && creation.Type == current;
    }

    public static double Similarity(CodeUnit first, CodeUnit second) => Score(Fingerprints(first), Fingerprints(second));
    private static double Score(HashSet<string> first, HashSet<string> second)
    {
        int shared = first.Count(second.Contains);
        int total = first.Count + second.Count - shared;
        return total == 0 ? 0 : shared / (double)total;
    }

    public static IReadOnlyList<Duplicate> Find(IReadOnlyList<CodeUnit> units, double threshold = .8, int minLines = 4, int minNodes = 20)
    {
        if (!double.IsFinite(threshold) || threshold is <= 0 or > 1 || minLines < 1 || minNodes < 1)
            throw new ArgumentException("Use threshold (0,1] and positive minimum sizes.");
        var candidates = units.Where(u => u.EndLine - u.StartLine + 1 >= minLines && u.Body.DescendantNodesAndSelf().Count() >= minNodes)
            .Select(u => (unit: u, shapes: Fingerprints(u))).ToArray();
        var found = new List<Duplicate>();
        for (int i = 0; i < candidates.Length; i++)
            for (int j = i + 1; j < candidates.Length; j++)
            {
                double score = Score(candidates[i].shapes, candidates[j].shapes);
                if (score >= threshold) found.Add(new(candidates[i].unit.Id, candidates[j].unit.Id, score));
            }
        return found.OrderByDescending(d => d.Similarity).ThenBy(d => d.First, StringComparer.Ordinal).ToArray();
    }
}
