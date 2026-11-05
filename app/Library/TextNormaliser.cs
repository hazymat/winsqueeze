using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace WinSqueeze.Library;

public static class TextNormaliser
{
    // Lower case, accents removed: "Dvořák" -> "dvorak"
    public static string Normalise(string text)
    {
        var decomposed = (text ?? "").Normalize(NormalizationForm.FormD);
        var plainBuilder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed) if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) plainBuilder.Append(character);
        return plainBuilder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant().Trim();
    }

    // Splits "Keith Jarrett, Gary Peacock & Jack DeJohnette" on the configured separators; word separators need spaces around them
    public static List<string> SplitNames(string names, string separatorList)
    {
        var separatorPatterns = separatorList.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(separator => char.IsLetter(separator[0]) ? $@"\s+{Regex.Escape(separator)}\s+" : $@"\s*{Regex.Escape(separator)}\s*");
        var splitPattern = string.Join("|", separatorPatterns);
        var parts = splitPattern == "" ? new[] { names } : Regex.Split(names ?? "", splitPattern, RegexOptions.IgnoreCase);
        return parts.Select(part => part.Trim()).Where(part => part != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
