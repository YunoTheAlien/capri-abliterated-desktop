#!/usr/bin/env bash
# Packs a Linux build of Capri into one installer file, Capri-<version>-linux-x64.run: setup.sh with the build after it.
#
#   packaging/linux/make-installer.sh /absolute/build-folder /absolute/output-folder
#
# The build folder is what ci/build-linux.sh made. Its version has to be the fork's (app/AssemblyInfo.cs, or CAPRI_VERSION),
# and it is read out of the build itself, so a stock upstream build can never be packed by mistake.
set -euo pipefail

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
build=${1:?Usage: make-installer.sh /absolute/build-folder /absolute/output-folder}
output=${2:?Usage: make-installer.sh /absolute/build-folder /absolute/output-folder}
[[ $build == /* && $output == /* ]] || { echo 'Both folders must be absolute.' >&2; exit 2; }
[[ -x $build/Capri && -f $build/Capri.dll ]] || { echo "$build isn't a Linux build of Capri (no Capri and Capri.dll)." >&2; exit 2; }

version=${CAPRI_VERSION:-}
if [[ -z $version && -f $root/app/AssemblyInfo.cs ]]; then
    version=$(sed -n 's/.*AssemblyFileVersion("\([0-9]*\.[0-9]*\.[0-9]*\)").*/\1/p' "$root/app/AssemblyInfo.cs" | head -n 1)
fi
[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "No usable version (got \"$version\"). Set CAPRI_VERSION." >&2; exit 2; }
# The assembly's file version is stored as UTF-16; ci/build-linux.sh and the csproj stamp it from the same AssemblyInfo.cs line.
stamped=$(python3 -I - "$build/Capri.dll" "$version" <<'EOF'
import sys
data = open(sys.argv[1], "rb").read()
print("yes" if (sys.argv[2] + ".0").encode("utf-16-le") in data else "no")
EOF
)
[[ $stamped == yes ]] || { echo "$build is not Capri $version (its Capri.dll doesn't carry that version). Build it with ci/build-linux.sh." >&2; exit 2; }

mkdir -p "$output"
installer=$output/Capri-$version-linux-x64.run
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT

# Her icon rides inside the build, so the installer has one file to put in the icon theme.
cp "$root/cross/icon.png" "$work/capri.png"
tar --sort=name --owner=0 --group=0 --numeric-owner --mtime='2000-01-01 00:00Z' \
    -C "$build" -cf "$work/payload.tar" . \
    && tar --owner=0 --group=0 --numeric-owner --mtime='2000-01-01 00:00Z' -C "$work" -rf "$work/payload.tar" ./capri.png
gzip -n -9 < "$work/payload.tar" > "$work/payload.tar.gz"
sha=$(sha256sum "$work/payload.tar.gz" | cut -d' ' -f1)

# setup.sh is CRLF-proofed here: a checkout with carriage returns would break /bin/sh on every line.
sed -e 's/\r$//' -e "s/@VERSION@/$version/" -e "s/@PAYLOAD_SHA256@/$sha/" "$root/packaging/linux/setup.sh" > "$work/head.sh"
grep -q "^VERSION='$version'\$" "$work/head.sh" && grep -q "^PAYLOAD_SHA256='$sha'\$" "$work/head.sh" \
    || { echo 'setup.sh no longer has the VERSION / PAYLOAD_SHA256 lines this expects.' >&2; exit 1; }
sh -n "$work/head.sh"
{ cat "$work/head.sh"; printf '__CAPRI_PAYLOAD_BELOW__\n'; cat "$work/payload.tar.gz"; } > "$installer.part"
chmod 755 "$installer.part"
mv -f "$installer.part" "$installer"
sha256sum "$installer" | sed "s|  $output/|  |" > "$installer.sha256"
printf 'Made %s (%s MB)\n' "$installer" "$(( $(stat -c %s "$installer") >> 20 ))"
cat "$installer.sha256"
