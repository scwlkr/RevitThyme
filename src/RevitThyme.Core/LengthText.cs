using System.Globalization;
using System.Text.RegularExpressions;

namespace RevitThyme.Core;

public static class LengthText
{
    public static string Format(double feet, string units = "ft/in")
    {
        if (units == "mm") return (feet * 304.8).ToString("0.###", CultureInfo.InvariantCulture);
        if (units == "m") return (feet * 0.3048).ToString("0.######", CultureInfo.InvariantCulture);
        long eighths = (long)Math.Round(Math.Abs(feet) * 96, MidpointRounding.AwayFromZero);
        long inches = eighths / 8 % 12, fraction = eighths % 8;
        string suffix = fraction == 0 ? "" : fraction % 4 == 0 ? " 1/2" : fraction % 2 == 0 ? $" {fraction / 2}/4" : $" {fraction}/8";
        return $"{(feet < 0 && eighths != 0 ? "-" : "")}{eighths / 96}' {inches}{suffix}\"";
    }
    public static double Parse(string text, string units = "ft/in")
    {
        text = text.Trim();
        double value;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            value = units == "mm" ? value / 304.8 : units == "m" ? value / 0.3048 : value;
        else if (units == "ft/in")
        {
            var match = Regex.Match(text, "^([+-]?)\\s*(?:(\\d+(?:\\.\\d+)?)'\\s*)?(?:(\\d+(?:\\.\\d+)?)\\s*)?(?:(\\d+)/(\\d+)\\s*)?\"?$", RegexOptions.CultureInvariant);
            if (!match.Success || !match.Groups.Cast<Group>().Skip(2).Any(g => g.Success)) throw new FormatException("Enter feet/inches, decimal feet, or choose metric units.");
            double Number(int group) => match.Groups[group].Success ? double.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture) : 0;
            double denominator = Number(5);
            if (match.Groups[4].Success && denominator == 0) throw new FormatException("Fraction denominator must be positive.");
            value = Number(2) + (Number(3) + (denominator > 0 ? Number(4) / denominator : 0)) / 12;
            if (match.Groups[1].Value == "-") value = -value;
        }
        else throw new FormatException("Enter a numeric metric length.");
        if (!double.IsFinite(value) || Math.Abs(value) > 1000000) throw new FormatException("Length is outside the supported range.");
        return value;
    }
}
