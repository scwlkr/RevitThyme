using System.Reflection;
using System.Runtime.Loader;
using Autodesk.Revit.UI;

namespace RevitThyme.ImageExportLoader;

// Startup-only qualification isolation; never replaces or hot-loads a working host.
public sealed class Loader : IExternalApplication
{
    private IExternalApplication? application;
    public Result OnStartup(UIControlledApplication app)
    {
        string? folder = Environment.GetEnvironmentVariable("REVITTHYME_IMAGE_TEST_RUN");
        if (folder is null) return Result.Succeeded;
        string bundle = System.IO.Path.Combine(folder, "bundle");
        var context = new CandidateContext(bundle);
        var assembly = context.LoadFromAssemblyPath(System.IO.Path.Combine(bundle, "RevitThyme.RevitIntegration.dll"));
        application = (IExternalApplication)Activator.CreateInstance(assembly.GetType("RevitThyme.RevitIntegration.ImageExportQualificationApplication", true)!)!;
        return application.OnStartup(app);
    }
    public Result OnShutdown(UIControlledApplication app) => application?.OnShutdown(app) ?? Result.Succeeded;
    private sealed class CandidateContext(string directory) : AssemblyLoadContext("Image export qualification", false)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            // Revit and framework types must retain their host identities.
            if (name.Name is null || !name.Name.StartsWith("RevitThyme", StringComparison.Ordinal)) return null;
            string path = System.IO.Path.Combine(directory, name.Name + ".dll");
            return System.IO.File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }
}
