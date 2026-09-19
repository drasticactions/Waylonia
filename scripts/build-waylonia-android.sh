#!/usr/bin/env bash
#
# build-waylonia-android.sh [OPTIONS]
#
# Publishes src/Waylonia.Android as a Release APK for android-arm64 into
# artifacts/. Needs the android workload, a JDK and the Android SDK. The APK
# is signed with the SDK's debug key unless --keystore and --alias name a
# keystore, whose passwords come from WAYLONIA_ANDROID_KEY_PASS and
# WAYLONIA_ANDROID_STORE_PASS.
#
#   --version V      version to stamp, default 0.1.0-local.g<commit>
#   --keystore PATH  the keystore to sign with (AndroidSigningKeyStore)
#   --alias NAME     the key alias in it (AndroidSigningKeyAlias)
#   --x64            also publish the android-x64 APK
#   --out DIR        where the APKs are written, default artifacts/
#

set -euo pipefail

source "$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/release-common.sh"

project=src/Waylonia.Android
rids=(android-arm64)
version=
keystore=
alias=
out="$root/artifacts"

while [ $# -gt 0 ]; do
    case "$1" in
        --version) version=${2:?--version needs a value}; shift 2 ;;
        --keystore) keystore=${2:?--keystore needs a value}; shift 2 ;;
        --alias) alias=${2:?--alias needs a value}; shift 2 ;;
        --x64) rids+=(android-x64); shift ;;
        --out) out=${2:?--out needs a value}; shift 2 ;;
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

: "${version:=$(local_version)}"

signing=()
if [ -n "$keystore" ] || [ -n "$alias" ]; then
    if [ -z "$keystore" ] || [ -z "$alias" ]; then
        echo "--keystore and --alias go together." >&2
        exit 1
    fi

    if [ -z "${WAYLONIA_ANDROID_KEY_PASS:-}" ] || [ -z "${WAYLONIA_ANDROID_STORE_PASS:-}" ]; then
        echo "set WAYLONIA_ANDROID_KEY_PASS and WAYLONIA_ANDROID_STORE_PASS for $keystore." >&2
        exit 1
    fi

    signing=(
        -p:AndroidKeyStore=true
        -p:AndroidSigningKeyStore="$keystore"
        -p:AndroidSigningKeyAlias="$alias"
        -p:AndroidSigningKeyPass="env:WAYLONIA_ANDROID_KEY_PASS"
        -p:AndroidSigningStorePass="env:WAYLONIA_ANDROID_STORE_PASS"
    )
fi

mkdir -p "$out"
out=$(cd "$out" && pwd)
stage="$out/stage-waylonia-android"
rm -rf "$stage"
trap 'rm -rf "$stage"' EXIT

echo "version $version, rids ${rids[*]}$([ -n "$keystore" ] && echo ", signed with $alias" || echo ", debug-signed")"
echo

apks=()
for rid in "${rids[@]}"; do
    echo "publishing $project for $rid"
    dotnet publish "$root/$project" -c Release -r "$rid" -p:Version="$version" \
        -p:AndroidPackageFormat=apk ${signing[@]+"${signing[@]}"} -o "$stage/$rid" --nologo -v quiet

    apk=$(find "$stage/$rid" -maxdepth 1 -name '*-Signed.apk' | head -1)
    if [ -z "$apk" ]; then
        echo "no signed APK in $stage/$rid" >&2
        exit 1
    fi

    cp "$apk" "$out/waylonia-$version-$rid.apk"
    apks+=("$out/waylonia-$version-$rid.apk")
done

report_files "${apks[@]}"
