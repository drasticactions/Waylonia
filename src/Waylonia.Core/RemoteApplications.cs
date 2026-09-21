using System.Text;
using Basin.Freedesktop;

namespace Waylonia;

internal sealed class RemoteApplications
{
    private const char Mark = '\u0001';

    private readonly HashSet<DesktopEntry> _tryExecMissing;

    private RemoteApplications(IReadOnlyList<DesktopEntry> entries, HashSet<DesktopEntry> tryExecMissing)
    {
        Entries = entries;
        _tryExecMissing = tryExecMissing;
    }

    public IReadOnlyList<DesktopEntry> Entries { get; }

    public static string Script(DesktopLocale locale)
    {
        var lang = locale.IsNone || !locale.Lang.All(char.IsAsciiLetter) ? null : locale.Lang;
        var localized = lang is null
            ? "/^[A-Za-z][-A-Za-z0-9]*\\[/d"
            : $"/^[A-Za-z][-A-Za-z0-9]*\\[/{{/^[A-Za-z][-A-Za-z0-9]*\\[{lang}/!d;}}";
        return
            "IFS=:; for d in ${XDG_DATA_HOME:-$HOME/.local/share} ${XDG_DATA_DIRS:-/usr/local/share:/usr/share}; do " +
            "case $d in /*) ;; *) continue;; esac; a=\"$d/applications\"; [ -d \"$a\" ] || continue; " +
            "find \"$a\" -name '*.desktop' 2>/dev/null | while IFS= read -r f; do " +
            "[ -f \"$f\" ] || continue; ok=1; t=$(sed -n 's/^TryExec=//p' \"$f\" | head -n 1); " +
            "if [ -n \"$t\" ]; then case $t in /*) [ -x \"$t\" ] || ok=0;; *) command -v \"$t\" >/dev/null 2>&1 || ok=0;; esac; fi; " +
            "printf '\\001%s\\001%s\\001%s\\n' \"$a\" \"$f\" \"$ok\"; " +
            $"sed -e '/^#/d' -e '{localized}' \"$f\"; printf '\\n'; " +
            "done; done";
    }

    public static RemoteApplications Parse(string output, DesktopLocale locale)
    {
        ArgumentNullException.ThrowIfNull(output);
        var entries = new List<DesktopEntry>();
        var missing = new HashSet<DesktopEntry>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        string? root = null;
        string? path = null;
        var resolved = true;
        var text = new StringBuilder();

        void Flush()
        {
            if (root is null || path is null)
            {
                return;
            }

            var id = IdOf(root, path);
            if (id is not null
                && taken.Add(id)
                && DesktopEntryReader.ParseText(text.ToString(), path, id, locale) is { } entry)
            {
                entries.Add(entry);
                if (!resolved)
                {
                    missing.Add(entry);
                }
            }

            text.Clear();
        }

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length > 0 && line[0] == Mark)
            {
                Flush();
                var parts = line.Split(Mark);
                root = parts.Length > 1 ? parts[1] : null;
                path = parts.Length > 2 ? parts[2] : null;
                resolved = parts.Length <= 3 || parts[3] != "0";
                continue;
            }

            text.Append(line).Append('\n');
        }

        Flush();
        entries.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return new RemoteApplications(entries, missing);
    }

    public IReadOnlyList<DesktopEntry> Listable(IReadOnlySet<string>? currentDesktop)
    {
        var listable = new List<DesktopEntry>();
        foreach (var entry in Entries)
        {
            if (entry.IsListable(currentDesktop, _ => !_tryExecMissing.Contains(entry)))
            {
                listable.Add(entry);
            }
        }

        return listable;
    }

    private static string? IdOf(string root, string path)
    {
        var prefix = root.EndsWith('/') ? root : root + "/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.Length == prefix.Length)
        {
            return null;
        }

        return path[prefix.Length..].Replace('/', '-');
    }
}
