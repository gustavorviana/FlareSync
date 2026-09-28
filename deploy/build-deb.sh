#!/usr/bin/env bash
# Builds the Debian package from a linux-x64 publish directory.
# Usage: deploy/build-deb.sh <version> <publish-dir> <output-dir>
# MAINTAINER (optional) sets the Maintainer field, e.g. "Name <name@example.com>".
set -euo pipefail

version=$1
publish=$2
output=$3
maintainer=${MAINTAINER:-"FlareSync <noreply@github.com>"}
deploy=$(cd "$(dirname "$0")" && pwd)

root=$(mktemp -d)
trap 'rm -rf "$root"' EXIT

install -Dm755 "$publish/flaresync" "$root/opt/flaresync/flaresync"
install -Dm644 "$deploy/flaresync.service" "$root/usr/lib/systemd/system/flaresync.service"
install -Dm644 "$deploy/../README.md" "$root/usr/share/doc/flaresync/README.md"
mkdir -p "$root/usr/bin"
ln -s /opt/flaresync/flaresync "$root/usr/bin/flaresync"

size=$(du -sk "$root" | cut -f1)
mkdir -p "$root/DEBIAN"
sed -e "s|@VERSION@|$version|" -e "s|@MAINTAINER@|$maintainer|" -e "s|@SIZE@|$size|" \
    "$deploy/debian/control" >"$root/DEBIAN/control"
install -m755 "$deploy/debian/postinst" "$deploy/debian/prerm" "$deploy/debian/postrm" "$root/DEBIAN/"

mkdir -p "$output"
dpkg-deb --build --root-owner-group "$root" "$output/flaresync_${version}_amd64.deb"
