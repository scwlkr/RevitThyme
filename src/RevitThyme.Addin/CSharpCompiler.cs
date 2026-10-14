using System.Reflection;
using System.IO;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RevitThyme.Core;

namespace RevitThyme.Execution;

// Shared with portable compiler tests by source link; no Autodesk dependency.
public sealed class CSharpCompiler
{
    public sealed class Submission(AssemblyLoadContext context, MethodInfo method) : IDisposable
    {
        public object? Run(params object[] arguments)
        {
            try { return method.Invoke(null, arguments); }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        public void Dispose() => context.Unload();
    }
    public static (Submission? Code, CodeDiagnostic[] Diagnostics) Compile(string source, string signature, string imports, Assembly[] assemblies)
    {
        string wrapped = imports + "\npublic static class Submission { public static object Run(" + signature + ") {\n#line 1 \"submission.cs\"\n"
            + source + "\n#line default\n} }";
        string runtimeFolder = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtime = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? typeof(object).Assembly.Location)
            .Split(Path.PathSeparator).Where(p => string.Equals(Path.GetDirectoryName(p), runtimeFolder, StringComparison.OrdinalIgnoreCase));
        var paths = runtime.Concat(assemblies.Select(a => a.Location)).Distinct(StringComparer.OrdinalIgnoreCase);
        var references = paths.Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("RevitThyme.Submission." + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(wrapped, new CSharpParseOptions(LanguageVersion.CSharp14))], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: false, deterministic: true));
        using var stream = new MemoryStream(); var emitted = compilation.Emit(stream);
        var diagnostics = emitted.Diagnostics.Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Take(12)
            .Select(d => { var pos = d.Location.GetMappedLineSpan().StartLinePosition;
                string message = d.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
                return new CodeDiagnostic(d.Id, d.Severity.ToString(), pos.Line + 1, pos.Character + 1, message[..Math.Min(240, message.Length)]); }).ToArray();
        if (!emitted.Success) return (null, diagnostics);
        var context = new SubmissionContext(assemblies); stream.Position = 0;
        try { return (new(context, context.LoadFromStream(stream).GetType("Submission")!.GetMethod("Run")!), diagnostics); }
        catch { context.Unload(); throw; }
    }
    private sealed class SubmissionContext(Assembly[] shared) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name) => shared.FirstOrDefault(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), name));
    }
}
