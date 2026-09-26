#!/usr/bin/env bash
#
# install-waylonia.sh [OPTIONS]
#
# Publishes src/Waylonia.Desktop with NativeAOT for this machine and installs
# it for the current user: the publish folder goes to PREFIX/lib/waylonia,
# PREFIX/bin/waylonia links to the binary in it, and on Linux waylonia.desktop
# goes to $XDG_DATA_HOME/applications with Exec pointing at that link, which
# the GlobalShortcuts portal needs to accept the app id. Running it again
# replaces the previous install.
#
#   --version V   version to stamp, default 0.1.0-local.g<commit>
#   --prefix DIR  install prefix, default ~/.local
#   --uninstall   remove what an earlier run installed and exit
#

set -euo pipefail

source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/release-common.sh"

project=src/Waylonia.Desktop
version=
prefix="$HOME/.local"
uninstall=0

while [ $# -gt 0 ]; do
    case "$1" in
        --version) version=${2:?--version needs a value}; shift 2 ;;
        --prefix) prefix=${2:?--prefix needs a value}; shift 2 ;;
        --uninstall) uninstall=1; shift ;;
        -h|--help)
            sed -n '3,16p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "unknown argument '$1'" >&2
            exit 1
            ;;
    esac
done

name=$(program_name "$project")
rid=$(host_rid)
libdir="$prefix/lib/$name"
bindir="$prefix/bin"
link="$bindir/$name"
applications="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
entry="$applications/$name.desktop"

refresh_desktop_database() {
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database -q "$applications" || true
    fi
}

if [ "$uninstall" -eq 1 ]; then
    rm -f "$link"
    rm -rf "$libdir"
    case "$rid" in
        linux-*)
            rm -f "$entry"
            refresh_desktop_database
            ;;
    esac
    echo "removed $name from $prefix"
    exit 0
fi

: "${version:=$(local_version)}"

stage="$prefix/lib/.$name-stage"
old="$prefix/lib/.$name-old"
rm -rf "$stage" "$old"
trap 'rm -rf "$stage" "$old"' EXIT

echo "version $version, rid $rid"
echo
echo "publishing $project"
mkdir -p "$prefix/lib"
publish_program "$project" "$stage" "$version" "$rid"

if [ -e "$libdir" ]; then
    mv "$libdir" "$old"
fi
mv "$stage" "$libdir"

mkdir -p "$bindir"
ln -sfn "$(program_binary "$libdir" "$name")" "$link"

case "$rid" in
    linux-*)
        if [ -f "$libdir/$name.desktop" ]; then
            mkdir -p "$applications"
            sed "s|^Exec=$name\b|Exec=$link|" "$libdir/$name.desktop" > "$entry"
            refresh_desktop_database
        else
            echo "warning: $name.desktop is not in the publish, so the GlobalShortcuts portal will refuse the app id." >&2
        fi
        ;;
esac

echo
echo "installed $("$link" --version) to $libdir"
echo "  $link"
if [ -f "$entry" ]; then
    echo "  $entry"
fi

case ":$PATH:" in
    *":$bindir:"*) ;;
    *) echo "warning: $bindir is not on PATH." >&2 ;;
esac
