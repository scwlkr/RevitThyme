using System.Security.Cryptography;
using System.Text;
using System.Collections.Frozen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RevitThyme.Quality;

public sealed class CodeUnit
{
    internal CodeUnit(string path, string name, SyntaxNode declaration, SyntaxNode body, string source)
    {
        Path = path; Name = name; Declaration = declaration; Body = body; Source = source;
        StartLine = declaration.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        EndLine = declaration.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
    }
    public string Path { get; }
    public string Name { get; }
    public int StartLine { get; }
    public int EndLine { get; }
    public string Id => $"{Path}:{StartLine}:{Name}";
    public int Complexity => SourceAnalysis.OwnedNodes(this).Count(SourceAnalysis.IsDecision) + 1;
    internal SyntaxNode Declaration { get; }
    internal SyntaxNode Body { get; }
    internal string Source { get; }
}

public static class SourceAnalysis
{
    public static IReadOnlyList<CodeUnit> Parse(string source, string path)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), path);
        var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
        var units = new List<CodeUnit>();
        foreach (var node in tree.GetRoot().DescendantNodes())
            if (Body(node) is { } body) units.Add(new CodeUnit(path, Name(node), node, body, source));
        return units;
    }

    internal static SyntaxNode? Body(SyntaxNode node) => node switch
    {
        BaseMethodDeclarationSyntax m => BlockOrExpression(m.Body, m.ExpressionBody),
        LocalFunctionStatementSyntax f => BlockOrExpression(f.Body, f.ExpressionBody),
        AccessorDeclarationSyntax a => BlockOrExpression(a.Body, a.ExpressionBody),
        PropertyDeclarationSyntax p => BlockOrExpression(null, p.ExpressionBody),
        IndexerDeclarationSyntax i => BlockOrExpression(null, i.ExpressionBody),
        LambdaExpressionSyntax l => l.Body,
        AnonymousMethodExpressionSyntax a => a.Block,
        _ => null
    };

    private static SyntaxNode? BlockOrExpression(BlockSyntax? block, ArrowExpressionClauseSyntax? expression)
        => (SyntaxNode?)block ?? expression?.Expression;

    private static string Name(SyntaxNode node)
    {
        return string.Join('.', node.AncestorsAndSelf().Reverse().Select(NamePart).Where(n => n is not null));
    }

    private static string? NamePart(SyntaxNode node) => node switch
    {
        BaseNamespaceDeclarationSyntax ns => ns.Name.ToString(),
        TypeDeclarationSyntax type => type.Identifier.Text,
        AnonymousFunctionExpressionSyntax a => "lambda@" + (a.GetLocation().GetLineSpan().StartLinePosition.Line + 1)
            + ":" + a.GetLocation().GetLineSpan().StartLinePosition.Character,
        _ => MemberName(node)
    };

    private static string? MemberName(SyntaxNode node) => node switch
        {
            MethodDeclarationSyntax m => m.Identifier.Text,
            ConstructorDeclarationSyntax => ".ctor",
            DestructorDeclarationSyntax => ".dtor",
            LocalFunctionStatementSyntax f => f.Identifier.Text,
            PropertyDeclarationSyntax p => p.Identifier.Text,
            IndexerDeclarationSyntax => "this[]",
            AccessorDeclarationSyntax a => a.Keyword.Text,
            _ => OperatorName(node)
        };

    private static string? OperatorName(SyntaxNode node) => node switch
    {
        OperatorDeclarationSyntax o => "operator" + o.OperatorToken.Text,
        ConversionOperatorDeclarationSyntax c => "operator" + c.Type,
        _ => null
    };

    internal static IEnumerable<SyntaxNode> OwnedNodes(CodeUnit unit)
    {
        var pending = new Stack<SyntaxNode>(); pending.Push(unit.Body);
        if (unit.Declaration is ConstructorDeclarationSyntax { Initializer: { } initializer }) pending.Push(initializer);
        while (pending.TryPop(out var node))
        {
            if (node != unit.Declaration && Body(node) is not null) continue;
            yield return node;
            foreach (var child in node.ChildNodes().Reverse()) pending.Push(child);
        }
    }

    private static readonly FrozenSet<SyntaxKind> Decisions = new[]
    {
        SyntaxKind.IfStatement, SyntaxKind.ForStatement, SyntaxKind.ForEachStatement, SyntaxKind.ForEachVariableStatement,
        SyntaxKind.WhileStatement, SyntaxKind.DoStatement, SyntaxKind.CatchClause, SyntaxKind.ConditionalExpression,
        SyntaxKind.ConditionalAccessExpression, SyntaxKind.WhenClause, SyntaxKind.CaseSwitchLabel,
        SyntaxKind.CasePatternSwitchLabel, SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression,
        SyntaxKind.CoalesceExpression, SyntaxKind.CoalesceAssignmentExpression, SyntaxKind.AndPattern, SyntaxKind.OrPattern
    }.ToFrozenSet();

    internal static bool IsDecision(SyntaxNode node) => Decisions.Contains(node.Kind())
        || node is SwitchExpressionArmSyntax { Pattern: not DiscardPatternSyntax };

    public static IReadOnlyList<CodeUnit> ReadTree(string root, string source = "src")
    {
        string directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, source));
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        var files = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Split(System.IO.Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj"))
            .Order(StringComparer.Ordinal).ToArray();
        if (files.Length == 0) throw new InvalidDataException("No C# source files found.");
        return files.SelectMany(p => Parse(File.ReadAllText(p), System.IO.Path.GetRelativePath(root, p).Replace('\\', '/'))).ToArray();
    }

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
