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

    // Splits "Keith Jarrett, Gary Peacock & Jack DeJohnette" on the configured separators; word separators need spaces around them.
    // A comma part shaped "Surname, Forename(s)" (one word before the comma: "Kapustin, Nikolai", "Tchaikovsky, P.I.", "Bach, Johann Sebastian") is one person, turned round to "Nikolai Kapustin" so it matches tags written that way
    static readonly Regex invertedName = new(@"^(?<surname>[^\s,]+)\s*,\s*(?<forenames>[^\s,]+(?:\s+[^\s,]+){0,2})$");
    public static List<string> SplitNames(string names, string separatorList)
    {
        var separators = separatorList.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var splitPattern = string.Join("|", separators.Where(separator => separator != ",").Select(separator => char.IsLetter(separator[0]) ? $@"\s+{Regex.Escape(separator)}\s+" : $@"\s*{Regex.Escape(separator)}\s*"));
        var parts = splitPattern == "" ? new[] { names ?? "" } : Regex.Split(names ?? "", splitPattern, RegexOptions.IgnoreCase);
        var splitsOnComma = separators.Contains(",");
        return parts.SelectMany(part => { var inverted = invertedName.Match(part.Trim()); return inverted.Success ? new[] { inverted.Groups["forenames"].Value + " " + inverted.Groups["surname"].Value } : splitsOnComma ? part.Split(',') : new[] { part }; })
            .Select(part => part.Trim()).Where(part => part != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
