using System.Text;

namespace Dockyard.Application;

/// <summary>
/// The command line that opens a saved layout: <c>Dockyard.exe --layout "name"</c>. Shortcut files are written
/// with <see cref="Format"/> and the program reads them back with <see cref="ParseLayout"/>.
/// </summary>
public static class LaunchCommand
{
    private const string LayoutOption = "--layout";

    /// <summary>The arguments that open the layout, quoted so that <see cref="ParseLayout"/> gets the name back.</summary>
    public static string Format(LayoutName layoutName) => $"{LayoutOption} {Quote(layoutName.Value)}";

    /// <summary>The layout name the arguments ask for; null if they ask for none.</summary>
    public static LayoutName? ParseLayout(IReadOnlyList<string> args)
    {
        for (var i = 0; i + 1 < args.Count; i++)
            if (args[i].Equals(LayoutOption, StringComparison.OrdinalIgnoreCase) && args[i + 1].Length > 0)
                return new LayoutName(args[i + 1]);
        return null;
    }

    /// <summary>Quotes by the rules of CommandLineToArgvW: backslashes only count before a quote.</summary>
    private static string Quote(string text)
    {
        var quoted = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in text)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            // Those before a quote are doubled, and the quote itself is escaped.
            if (c == '"') quoted.Append('\\', backslashes * 2 + 1);
            else quoted.Append('\\', backslashes);
            quoted.Append(c);
            backslashes = 0;
        }
        // Those before the closing quote are doubled too, or they would escape it.
        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
