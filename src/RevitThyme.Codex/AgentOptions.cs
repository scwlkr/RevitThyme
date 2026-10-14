using System.Diagnostics;
using System.Text.Json;
using RevitThyme.Core;

namespace RevitThyme.Codex;

public sealed record AgentOptions(string CodexExecutable, string McpExecutable, string SessionFile,
    string Workspace, string BundledSkills, string PersonalSkills, bool TrustedCode = false)
{
    private static string Toml(string value) => JsonSerializer.Serialize(value.Replace('\\', '/'));
    public ProcessStartInfo Launch()
    {
        foreach (string path in new[] { CodexExecutable, McpExecutable, SessionFile, Workspace, BundledSkills, PersonalSkills })
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Agent paths must be absolute.");
        SessionBinding.Load(SessionFile); // Reject stale/PID-reused targets before launching any runtime.
        if (!File.Exists(CodexExecutable) || !File.Exists(McpExecutable)) throw new FileNotFoundException("Install the Codex runtime and RevitThyme MCP component.");
        Directory.CreateDirectory(Workspace); Directory.CreateDirectory(PersonalSkills);
        var info = new ProcessStartInfo(CodexExecutable) { WorkingDirectory = Workspace };
        string codexHome = Path.Combine(Path.GetDirectoryName(Workspace)!, "codex-home");
        Directory.CreateDirectory(codexHome); info.Environment["CODEX_HOME"] = codexHome;
        info.Environment.Remove("OPENAI_API_KEY"); info.Environment.Remove("CODEX_API_KEY");
        foreach (string argument in new[] { "app-server", "--listen", "stdio://" }) info.ArgumentList.Add(argument);
        var args = new List<string> { "--session", SessionFile };
        string mcp = "mcp_servers={revitthyme={command=" + Toml(McpExecutable) + ",args=[" + string.Join(',', args.Select(Toml))
            + "],required=true,startup_timeout_sec=30,tool_timeout_sec=60}}";
        info.ArgumentList.Add("-c"); info.ArgumentList.Add(mcp);
        info.ArgumentList.Add("-c"); info.ArgumentList.Add("approval_policy=\"on-request\"");
        info.ArgumentList.Add("-c"); info.ArgumentList.Add("model_provider=\"openai\"");
        foreach (string feature in new[] { "shell_tool", "apps", "plugins", "multi_agent", "hooks", "computer_use", "browser_use", "image_generation", "code_mode" })
        { info.ArgumentList.Add("--disable"); info.ArgumentList.Add(feature); }
        return info;
    }
    public static string FindRuntime(string bundle)
    {
        string? explicitPath = Environment.GetEnvironmentVariable("REVITTHYME_CODEX_PATH");
        if (explicitPath is not null) return Path.GetFullPath(explicitPath);
        string packaged = Path.Combine(bundle, "codex", "codex.exe"); if (File.Exists(packaged)) return packaged;
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        { string path = Path.Combine(directory.Trim('"'), "codex.exe"); if (Path.IsPathFullyQualified(path) && File.Exists(path)) return path; }
        throw new FileNotFoundException("Codex runtime missing. Install the Agent package or set REVITTHYME_CODEX_PATH to codex.exe.");
    }
}
