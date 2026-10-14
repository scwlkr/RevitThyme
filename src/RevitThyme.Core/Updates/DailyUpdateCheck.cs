using System.Globalization;

namespace RevitThyme.Core;

public static class DailyUpdateCheck
{
    public static string CachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitThyme", "updates", "last-check.txt");
    public static bool Claim(string path, DateTimeOffset now)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Update cache path must be absolute.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (file.Length <= 128)
            {
                using var reader = new StreamReader(file, leaveOpen: true);
                if (DateTimeOffset.TryParse(reader.ReadToEnd(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var previous)
                    && now >= previous && now - previous < TimeSpan.FromDays(1)) return false;
            }
            file.Position = 0; file.SetLength(0);
            using var writer = new StreamWriter(file, leaveOpen: true); writer.Write(now.ToString("O", CultureInfo.InvariantCulture));
            return true;
        }
        catch (IOException) { return false; } // Another Revit window owns the check.
    }
}
