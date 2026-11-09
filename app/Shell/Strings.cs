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
    public static readonly (string code, string name)[] Languages = { ("en", "English"), ("de", "Deutsch") };
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
        foreach (var (name, value) in values) text = text.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.CurrentCulture));
        return text;
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
