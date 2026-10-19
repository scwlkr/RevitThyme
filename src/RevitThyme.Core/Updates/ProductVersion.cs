using System.Reflection;
using System.Text.RegularExpressions;

namespace RevitThyme.Core;

public static class ProductVersion
{
    public static string Current => typeof(ProductVersion).Assembly.GetName().Version!.ToString(3);
    public static string Build => typeof(ProductVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    public static string Stage => Build.Split('+')[0];
    public static Version ParseStable(string value)
    {
        if (value.Length > 64 || !Regex.IsMatch(value, @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$"))
            throw new ArgumentException("Use a stable version such as 0.9.0, without a leading v.");
        return Version.Parse(value);
    }
}
