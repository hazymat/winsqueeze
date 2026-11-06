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

    // A person's name in the chosen order: "surname" turns "Nikolai Kapustin" into "Kapustin, Nikolai" (last word is the surname; one-word names stay as they are)
    public static string PersonDisplay(string name, string nameOrder)
    {
        var words = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return nameOrder != "surname" || words.Length < 2 ? name.Trim() : words[^1] + ", " + string.Join(" ", words[..^1]);
    }

    // Splits "Keith Jarrett & Gary Peacock" on the configured separators; word separators need spaces around them.
    // People (composers, conductors) also split on commas, and with readSurnameFirst a comma part shaped "Surname, Forename(s)" ("Kapustin, Nikolai", "Tchaikovsky, P.I.") is one person, turned round to "Nikolai Kapustin" so it matches tags written that way.
    // Artists never split on commas or turn round, and a name with a comma stays whole ("Earth, Wind & Fire", "Crosby, Stills, Nash & Young")
    static readonly Regex invertedName = new(@"^(?<surname>[^\s,]+)\s*,\s*(?<forenames>[^\s,]+(?:\s+[^\s,]+){0,2})$");
    public static List<string> SplitNames(string names, string separatorList, bool personNames, bool readSurnameFirst)
    {
        names ??= "";
        if (!personNames && names.Contains(',')) return names.Trim() == "" ? new() : new() { names.Trim() };
        var separators = separatorList.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var splitPattern = string.Join("|", separators.Where(separator => separator != ",").Select(separator => char.IsLetter(separator[0]) ? $@"\s+{Regex.Escape(separator)}\s+" : $@"\s*{Regex.Escape(separator)}\s*"));
        var parts = splitPattern == "" ? new[] { names } : Regex.Split(names, splitPattern, RegexOptions.IgnoreCase);
        var splitsOnComma = personNames && separators.Contains(",");
        return parts.SelectMany(part => { var inverted = readSurnameFirst && personNames ? invertedName.Match(part.Trim()) : Match.Empty; return inverted.Success ? new[] { inverted.Groups["forenames"].Value + " " + inverted.Groups["surname"].Value } : splitsOnComma ? part.Split(',') : new[] { part }; })
            .Select(part => part.Trim()).Where(part => part != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
