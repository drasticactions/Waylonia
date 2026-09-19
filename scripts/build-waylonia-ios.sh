#!/usr/bin/env bash
#
# build-waylonia-ios.sh [OPTIONS]
#
# Publishes src/Waylonia.iOS for a device (ios-arm64) with NativeAOT and zips the
# app bundle into artifacts/. Only a Mac with Xcode and the ios workload can run
# it. The bundle is unsigned unless --key and --provision name a signing
# identity and a provisioning profile, which is what a device install needs.
#
#   --version V      version to stamp, default 0.1.0-local.g<commit>
#   --key NAME       the codesign identity (CodesignKey), e.g. "Apple Development"
#   --provision NAME the provisioning profile (CodesignProvision)
#   --out DIR        where the zip is written, default artifacts/
#

set -euo pipefail

source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/release-common.sh"

project=src/Waylonia.iOS
rid=ios-arm64
version=
key=
provision=
out="$root/artifacts"

while [ $# -gt 0 ]; do
    case "$1" in
        --version) version=${2:?--version needs a value}; shift 2 ;;
        --key) key=${2:?--key needs a value}; shift 2 ;;
        --provision) provision=${2:?--provision needs a value}; shift 2 ;;
        --out) out=${2:?--out needs a value}; shift 2 ;;
        -h|--help)
            sed -n '3,14p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "unknown argument '$1'" >&2
            exit 1
            ;;
    esac
done

case "$(host_rid)" in
    osx-*) ;;
    *)
        echo "the iOS head builds on a Mac with Xcode; this is $(host_rid)." >&2
        exit 1
        ;;
esac

: "${version:=$(local_version)}"

name=Waylonia
folder="$name-ios-$version-$rid"

mkdir -p "$out"
out=$(cd "$out" && pwd)
stage="$out/stage-$name-ios"
rm -rf "$stage"
trap 'rm -rf "$stage"' EXIT

signing=(-p:EnableCodeSigning=false)
if [ -n "$key" ] || [ -n "$provision" ]; then
    if [ -z "$key" ] || [ -z "$provision" ]; then
        echo "--key and --provision go together." >&2
        exit 1
    fi

    signing=(-p:CodesignKey="$key" -p:CodesignProvision="$provision")
fi

echo "version $version, rid $rid$([ -n "$key" ] && echo ", signed as $key" || echo ", unsigned")"
echo
echo "publishing $project"
dotnet publish "$root/$project" -c Release -r "$rid" -p:Version="$version" \
    "${signing[@]}" -o "$stage/publish" --nologo -v quiet

app=$(find "$stage/publish" -maxdepth 1 -name '*.app' | head -1)
if [ -z "$app" ]; then
    echo "no .app bundle in $stage/publish" >&2
    exit 1
fi

mkdir -p "$stage/$folder"
cp -R "$app" "$stage/$folder/"
cp "$root/LICENSE" "$stage/$folder/LICENSE"

make_zip "$out/$folder.zip" "$stage" "$folder"

report_files "$out/$folder.zip"
