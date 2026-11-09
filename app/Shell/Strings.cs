using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
namespace WinSqueeze;

// Text in the user's language (SPEC.md › Languages). The English text is the key; locales/<code>.json (embedded) maps it to the translation, and anything missing stays English.
// The web UI gets the same files and the chosen code as ?lang=
public static class L
{
    public static readonly (string code, string name)[] Languages = { ("en", "English"), ("de", "Deutsch"), ("uk", "Українська"), ("es", "Español"), ("fr", "Français"), ("ja", "日本語"), ("zh", "中文（简体）"), ("pl", "Polski"), ("it", "Italiano"), ("nl", "Nederlands"), ("pt", "Português"), ("sv", "Svenska"), ("cs", "Čeština") };
    public static string Code { get; private set; } = "en";
    static Dictionary<string, string> dictionary = new();

    // setting: "" follows Windows' display language, otherwise a code from Languages
    public static string Resolve(string setting) { var wanted = setting != "" ? setting : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName; return Languages.Any(language => language.code == wanted) ? wanted : "en"; }
    public static void Load(string setting)
    {
        Code = Resolve(setting); dictionary = new();
        if (Code == "en") return;
        using var stream = typeof(L).Assembly.GetManifestResourceStream($"locales.{Code}.json");
        if (stream != null) dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
    }

    public static string T(string english) => dictionary.TryGetValue(english, out var text) && text != "" ? text : english;
    // T("Restart to update to {version}", ("version", ready)) fills {names} after translating
    public static string T(string english, params (string name, object? value)[] values)
    {
        var text = T(english);
        // "форма1|форма2|форма3": languages with more than two plural forms pick one by {count}
        if (text.Contains('|') && long.TryParse(Convert.ToString(values.FirstOrDefault(pair => pair.name == "count").value, CultureInfo.InvariantCulture), out var count)) { var forms = text.Split('|'); text = forms[Math.Min(PluralForm(count), forms.Length - 1)]; }
        foreach (var (name, value) in values) text = text.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.CurrentCulture));
        return text;
    }
    // Which of a translation's "|" forms a count takes: one, few, many (Ukrainian, Polish and Czech); other languages have one form
    static int PluralForm(long count)
    {
        long lastDigit = count % 10, lastTwo = count % 100;
        bool few = lastDigit >= 2 && lastDigit <= 4 && (lastTwo < 12 || lastTwo > 14);
        return Code switch { "uk" => lastDigit == 1 && lastTwo != 11 ? 0 : few ? 1 : 2, "pl" => count == 1 ? 0 : few ? 1 : 2, "cs" => count == 1 ? 0 : count >= 2 && count <= 4 ? 1 : 2, _ => 0 };
    }
    // Singular or plural by count, both forms being keys
    public static string N(long count, string one, string other, params (string name, object? value)[] values) => T(count == 1 ? one : other, new[] { ("count", (object?)count) }.Concat(values).ToArray());

    // Translates the fixed English text a XAML window was written with: titles, labels, buttons, tooltips, headers and runs
    public static void Translate(DependencyObject root)
    {
        if (Code == "en") return;
        if (root is Window window) window.Title = T(window.Title);
        if (root is FrameworkElement { ToolTip: string tip } element) element.ToolTip = T(tip);
        switch (root)
        {
            case TextBlock textBlock when textBlock.Inlines.Count > 0 && textBlock.Inlines.All(inline => inline is Run): textBlock.Text = T(textBlock.Text); break;
            case TextBlock textBlock: TranslateInlines(textBlock.Inlines); break;
            case HeaderedContentControl { Header: string header } headered: headered.Header = T(header); break;
            case HeaderedItemsControl { Header: string itemsHeader } headeredItems: headeredItems.Header = T(itemsHeader); break;
        }
        if (root is ContentControl { Content: string content } contentControl && !(root is Window)) contentControl.Content = T(content);
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) Translate(child);
    }
    static void TranslateInlines(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Run run) run.Text = T(run.Text);
            else if (inline is Span span) TranslateInlines(span.Inlines);
            else if (inline is InlineUIContainer { Child: { } child }) Translate(child);
        }
    }
}
