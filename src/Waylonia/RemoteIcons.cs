using System.Text;

namespace Waylonia;

internal sealed record RemoteIcons(IReadOnlyList<RemoteIcon> Icons, IReadOnlySet<string> Missing)
{
    private const char Mark = '';
    private const char End = '';

    private static readonly string[] Sizes =
        ["48x48", "64x64", "32x32", "96x96", "128x128", "256x256", "24x24", "22x22", "16x16", "scalable"];

    public static bool IsSafeName(string name) =>
        name.Length > 0 && name.Length < 256
        && name.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '/' or '+' or '@');

    public static string Script(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var list = string.Join(' ', names.Where(IsSafeName).Distinct(StringComparer.Ordinal).Select(static name => $"'{name}'"));
        var sizes = string.Join(' ', Sizes);
        return
            "roots=\"$HOME/.icons\"; IFS=:; for d in ${XDG_DATA_HOME:-$HOME/.local/share} ${XDG_DATA_DIRS:-/usr/local/share:/usr/share}; do " +
            "case $d in /*) roots=\"$roots $d/icons\";; esac; done; IFS=' '; " +
            "emit() { case $2 in *.png) e=png;; *.svg) e=svg;; *) return 1;; esac; " +
            "printf '\\001%s\\001%s\\n' \"$1\" \"$e\"; base64 \"$2\"; printf '\\002\\n'; return 0; }; " +
            "find_icon() { n=$1; case $n in /*) [ -f \"$n\" ] && emit \"$n\" \"$n\" && return; printf '\\001%s\\001-\\n' \"$n\"; return;; esac; " +
            $"for s in {sizes}; do for r in $roots; do [ -d \"$r\" ] || continue; for t in hicolor Adwaita breeze Papirus Yaru '*'; do " +
            "for f in \"$r\"/$t/$s/*/\"$n\".png \"$r\"/$t/$s/*/\"$n\".svg \"$r\"/$t/$s/\"$n\".png \"$r\"/$t/$s/\"$n\".svg; do " +
            "[ -f \"$f\" ] && emit \"$n\" \"$f\" && return; done; done; done; done; " +
            "for r in $roots; do for f in \"$r/$n.png\" \"$r/$n.svg\" \"$r/../pixmaps/$n.png\" \"$r/../pixmaps/$n.svg\"; do " +
            "[ -f \"$f\" ] && emit \"$n\" \"$f\" && return; done; done; printf '\\001%s\\001-\\n' \"$n\"; }; " +
            $"for n in {list}; do find_icon \"$n\"; done";
    }

    public static RemoteIcons Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var icons = new List<RemoteIcon>();
        var missing = new HashSet<string>(StringComparer.Ordinal);
        string? name = null;
        string? extension = null;
        var body = new StringBuilder();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length > 0 && line[0] == Mark)
            {
                var parts = line.Split(Mark);
                name = parts.Length > 1 ? parts[1] : null;
                extension = parts.Length > 2 ? parts[2] : null;
                body.Clear();
                if (name is not null && extension == "-")
                {
                    missing.Add(name);
                    name = null;
                }

                continue;
            }

            if (line.Length > 0 && line[0] == End)
            {
                if (name is not null && extension is "png" or "svg")
                {
                    try
                    {
                        icons.Add(new RemoteIcon(name, extension, Convert.FromBase64String(body.ToString())));
                    }
                    catch (FormatException)
                    {
                        missing.Add(name);
                    }
                }

                name = null;
                body.Clear();
                continue;
            }

            body.Append(line);
        }

        return new RemoteIcons(icons, missing);
    }
}
