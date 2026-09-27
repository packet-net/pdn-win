#!/usr/bin/env bash
# Checks that a built pdn-lin .deb carries what it must, from its listing and control file.
#
#   packaging/linux/check-deb.sh dist/pdn-lin_<version>_<arch>.deb
#
# Run by CI and by the release on every package it builds: a package that installs but has lost
# SkiaSharp draws nothing, one without the serial shim fails at the first serial key-down, and one
# without the udev rule leaves every CM108 operator root-only PTT.
set -euo pipefail

DEB="${1:?usage: check-deb.sh <deb>}"
NAME="$(basename "$DEB")"
dpkg-deb --info "$DEB"

# Capture once and match in memory: grep -q on a pipe would close it early and the SIGPIPE
# dpkg-deb takes for that would fail the pipeline under pipefail.
listing="$(dpkg-deb --contents "$DEB")"
failed=0
need() {
  if ! grep -qE "$1" <<<"$listing"; then
    echo "::error::$NAME has no $2"
    failed=1
  fi
}

need 'usr/lib/pdn-lin/pdn-win$' 'executable (usr/lib/pdn-lin/pdn-win)'
need 'usr/bin/pdn-lin -> ' '/usr/bin/pdn-lin link'
need 'usr/lib/pdn-lin/libSkiaSharp\.so$' 'SkiaSharp, which draws everything'
need 'usr/lib/pdn-lin/libHarfBuzzSharp\.so$' 'HarfBuzzSharp, which shapes the text'
need 'usr/lib/pdn-lin/libSystem\.IO\.Ports\.Native\.so$' 'serial port shim (serial PTT)'
need 'usr/share/applications/pdn-lin\.desktop$' 'desktop entry'
need 'usr/share/icons/hicolor/256x256/apps/pdn-lin\.png$' 'icon'
need 'usr/lib/udev/rules\.d/70-pdn-lin\.rules$' 'udev rule (CM108 and AIOC PTT)'
need 'usr/share/wireplumber/wireplumber\.conf\.d/50-pdn-lin\.conf$' 'WirePlumber 0.5 rule'
need 'usr/share/wireplumber/main\.lua\.d/51-pdn-lin\.lua$' 'WirePlumber 0.4 rule'

# build-deb.sh reads the libc6 floor out of the binaries; an unversioned libc6 means that came
# unstuck, and apt would install onto machines whose loader then refuses the program.
if ! dpkg-deb -f "$DEB" Depends | grep -qE 'libc6 \(>= [0-9]+\.[0-9]+\)'; then
  echo "::error::$NAME depends on an unversioned libc6"
  failed=1
fi

[ "$failed" = 0 ] && echo "$NAME: complete"
exit "$failed"
