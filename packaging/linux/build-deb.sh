#!/usr/bin/env bash
# Builds the pdn-lin .deb (pdn-win's Linux package) for one architecture, and a tarball of the
# same payload for distributions that do not take .debs.
#
#   packaging/linux/build-deb.sh <version> [amd64|arm64] [outdir]
#
# Produces <outdir>/pdn-lin_<version>_<arch>.deb and <outdir>/pdn-lin-<version>-linux-<arch>.tar.gz
# from a self-contained single-file build (no .NET runtime needed on the target), with a desktop
# entry and icons, the udev rule that lets the operator key a CM108 or AIOC and open its serial
# port, and the WirePlumber rule that keeps the desktop's sound server off the AIOC.
#
# Layout, as pdn-soundmodem's .deb: PublishSingleFile leaves the native libraries (SkiaSharp,
# HarfBuzzSharp, System.IO.Ports) loose beside the executable and .NET finds them relative to the
# executable's real path, so the payload lives in /usr/lib/pdn-lin/ and /usr/bin/pdn-lin is a
# symlink into it.
set -euo pipefail

VERSION="${1:?usage: build-deb.sh <version> [arch] [outdir]}"
ARCH="${2:-amd64}"

case "$ARCH" in
  amd64) RID=linux-x64; TARCH=x64 ;;
  arm64) RID=linux-arm64; TARCH=arm64 ;;
  *) echo "unsupported arch $ARCH (amd64 and arm64 are built)" >&2; exit 2 ;;
esac

command -v dpkg-deb >/dev/null || { echo "dpkg-deb not found - this needs a Debian-family host" >&2; exit 3; }
command -v readelf >/dev/null || { echo "readelf not found - install binutils" >&2; exit 3; }

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
OUTDIR="${3:-$ROOT/dist}"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

PKGDIR=/usr/lib/pdn-lin
DOCDIR=/usr/share/doc/pdn-lin

# The app's project, and the executable it publishes (its assembly name). PDN_SOUNDMODEM_ROOT
# builds against a local pdn-soundmodem checkout instead of the pinned packages, as
# -p:PdnSoundModemRoot does for dotnet build.
PROJECT="${PROJECT:-$ROOT/src/PdnWin/PdnWin.csproj}"
UPSTREAM=()
[ -n "${PDN_SOUNDMODEM_ROOT:-}" ] && UPSTREAM=("-p:PdnSoundModemRoot=$PDN_SOUNDMODEM_ROOT")
EXENAME="$(dotnet msbuild "$PROJECT" "${UPSTREAM[@]}" -getProperty:AssemblyName)"
[ -n "$EXENAME" ] || { echo "could not read the assembly name of $PROJECT" >&2; exit 4; }

# Invariant globalization: a self-contained build otherwise needs libicu, whose package name
# changes with every release of every distribution (libicu72, libicu74, libicu76), which a Depends
# line cannot say. Nothing here formats for a culture.
dotnet publish "$PROJECT" \
  --configuration Release \
  --runtime "$RID" \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:InvariantGlobalization=true \
  -p:Version="$VERSION" \
  -p:DebugType=none \
  -p:GenerateDocumentationFile=false \
  "${UPSTREAM[@]}" \
  --output "$STAGE/publish"

EXE="$STAGE/publish/$EXENAME"
[ -f "$EXE" ] || { echo "no $EXENAME in the publish output" >&2; exit 4; }

mkdir -p "$STAGE/root$PKGDIR" "$STAGE/root/usr/bin" "$STAGE/root$DOCDIR" "$STAGE/root/DEBIAN" \
         "$STAGE/root/usr/share/applications" \
         "$STAGE/root/usr/lib/udev/rules.d" \
         "$STAGE/root/usr/share/wireplumber/wireplumber.conf.d" \
         "$STAGE/root/usr/share/wireplumber/main.lua.d"

install -m 0755 "$EXE" "$STAGE/root$PKGDIR/$EXENAME"
for so in "$STAGE"/publish/*.so; do
  [ -e "$so" ] || continue
  install -m 0644 "$so" "$STAGE/root$PKGDIR/$(basename "$so")"
done
ln -s "..${PKGDIR#/usr}/$EXENAME" "$STAGE/root/usr/bin/pdn-lin"

install -m 0644 "$HERE/pdn-lin.desktop" "$STAGE/root/usr/share/applications/pdn-lin.desktop"
for png in "$HERE"/icons/*.png; do
  size="$(basename "$png" .png)"
  mkdir -p "$STAGE/root/usr/share/icons/hicolor/${size}x${size}/apps"
  install -m 0644 "$png" "$STAGE/root/usr/share/icons/hicolor/${size}x${size}/apps/pdn-lin.png"
done
install -m 0644 "$HERE/70-pdn-lin.rules" "$STAGE/root/usr/lib/udev/rules.d/70-pdn-lin.rules"
install -m 0644 "$HERE/50-pdn-lin.conf" "$STAGE/root/usr/share/wireplumber/wireplumber.conf.d/50-pdn-lin.conf"
install -m 0644 "$HERE/51-pdn-lin.lua" "$STAGE/root/usr/share/wireplumber/main.lua.d/51-pdn-lin.lua"
install -m 0644 "$HERE/copyright" "$STAGE/root$DOCDIR/copyright"

case "${SOURCE_DATE_EPOCH:-}" in
  ''|*[!0-9]*) CHANGELOG_DATE="$(date -R)" ;;
  *)           CHANGELOG_DATE="$(date -R --date="@$SOURCE_DATE_EPOCH")" ;;
esac
cat > "$STAGE/changelog.Debian" <<EOF
pdn-lin ($VERSION) unstable; urgency=medium

  * Release $VERSION. See https://github.com/packet-net/pdn-win/releases/tag/$VERSION

 -- Tom Fanning M0LTE <tom@m0lte.uk>  $CHANGELOG_DATE
EOF
gzip -9n -c "$STAGE/changelog.Debian" > "$STAGE/root$DOCDIR/changelog.Debian.gz"
chmod 0644 "$STAGE/root$DOCDIR/changelog.Debian.gz"

INSTALLED_SIZE="$(du -k -s --exclude=DEBIAN "$STAGE/root" | cut -f1)"

# --- library version floors, read from what was just published --------------------------------
# As pdn-soundmodem's build-deb.sh, for the same reason: the runtime and the native libraries set
# the glibc and libstdc++ floors, not this repository, and a Depends that understates them installs
# onto machines whose loader then refuses the binary. Every ELF in the package, found by its magic.
elf_files() {
  find "$STAGE/root" -type f -print | while IFS= read -r f; do
    [ "$(od -An -tx1 -N4 "$f" 2>/dev/null | tr -d ' \n')" = "7f454c46" ] && printf '%s\n' "$f"
  done
}

max_needed() {
  local family="$1" max="" v f
  while IFS= read -r f; do
    [ -n "$f" ] || continue
    v="$(readelf --version-info "$f" 2>/dev/null \
      | awk '/Version needs section/,0' \
      | grep -oE "${family}_[0-9][0-9.]*" \
      | sed "s/^${family}_//" \
      | sort -uV \
      | tail -1)"
    [ -n "$v" ] && max="$(printf '%s\n%s\n' "$max" "$v" | sort -uV | tail -1)"
  done <<EOF
$(elf_files)
EOF
  printf '%s' "$max"
}

GLIBC_MIN="$(max_needed GLIBC)"
GLIBCXX_MIN="$(max_needed GLIBCXX)"
[ -n "$GLIBC_MIN" ] || { echo "could not read a GLIBC floor from the staged package" >&2; exit 4; }
[ -n "$GLIBCXX_MIN" ] || { echo "could not read a GLIBCXX floor from the staged package" >&2; exit 4; }

# libstdc++ versions its symbols by C++ ABI, not by package version: the anchors are pdn-soundmodem's,
# measured against the distributions (Debian 10 GCC 8 tops out at 3.4.25, 11 at 3.4.28, 12 at
# 3.4.30, 13 at 3.4.33), and unmeasured points round up.
case "$GLIBCXX_MIN" in
  3.4|3.4.[0-9]|3.4.1[0-9]|3.4.2[01]) STDCXX_MIN=5 ;;
  3.4.22)     STDCXX_MIN=6 ;;
  3.4.23|3.4.24) STDCXX_MIN=7 ;;
  3.4.25)     STDCXX_MIN=8 ;;
  3.4.26)     STDCXX_MIN=9 ;;
  3.4.27|3.4.28) STDCXX_MIN=10 ;;
  3.4.29)     STDCXX_MIN=11 ;;
  3.4.30)     STDCXX_MIN=12 ;;
  3.4.31|3.4.32) STDCXX_MIN=13 ;;
  3.4.33)     STDCXX_MIN=14 ;;
  3.4.34)     STDCXX_MIN=15 ;;
  *) echo "unknown GLIBCXX_$GLIBCXX_MIN - extend the table in $0" >&2; exit 4 ;;
esac
echo "floors for $ARCH: libc6 >= $GLIBC_MIN, libstdc++6 >= $STDCXX_MIN (GLIBCXX_$GLIBCXX_MIN)"

# Avalonia on X11 needs libX11, libICE and libSM, and fontconfig to find fonts; the station needs
# ALSA. pipewire-bin (pw-dump) is only for going through PipeWire when the desktop holds a card.
cat > "$STAGE/root/DEBIAN/control" <<EOF
Package: pdn-lin
Version: $VERSION
Architecture: $ARCH
Maintainer: Tom Fanning M0LTE <tom@m0lte.uk>
Installed-Size: $INSTALLED_SIZE
Depends: libc6 (>= $GLIBC_MIN), libgcc-s1, libstdc++6 (>= $STDCXX_MIN), libasound2 | libasound2t64, libfontconfig1, libx11-6, libice6, libsm6
Recommends: pipewire-bin
Section: hamradio
Priority: optional
Homepage: https://github.com/packet-net/pdn-win
Description: AX.25 packet radio terminal with a built-in soundcard modem
 pdn-lin is pdn-win for Linux: a packet radio terminal on the packet.net
 AX.25 stack with pdn-soundmodem running inside it. Plug in a USB radio
 interface (an AIOC or a CM108 dongle) with an FM radio on it, choose the
 mode, set the levels with the level helper and connect.
 .
 A monitor, tabbed sessions, a heard list, spectrum and waterfall, all in
 dockable panes. FM modes: AFSK 1200 (plain, IL2P+CRC, FX.25) and QPSK 3600.
 .
 Installs a udev rule giving the logged-in user and the audio group the
 CM108 and AIOC PTT (hidraw) nodes and the AIOC's serial port, and a
 WirePlumber rule that keeps the desktop's sound server off the AIOC.
 .
 AGPL-3.0-or-later.
EOF

# udev only: the rule has to reach interfaces already plugged in. The desktop entry and the icons
# are picked up by desktop-file-utils' and the icon theme's own dpkg triggers, and WirePlumber
# reads its rule at its next start (the next login).
cat > "$STAGE/root/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "configure" ] && [ -d /run/udev ] && command -v udevadm >/dev/null; then
    udevadm control --reload-rules >/dev/null 2>&1 || true
    udevadm trigger --subsystem-match=hidraw --subsystem-match=tty --action=change >/dev/null 2>&1 || true
fi
exit 0
EOF
cat > "$STAGE/root/DEBIAN/postrm" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "remove" ] && [ -d /run/udev ] && command -v udevadm >/dev/null; then
    udevadm control --reload-rules >/dev/null 2>&1 || true
fi
exit 0
EOF
chmod 0755 "$STAGE/root/DEBIAN/postinst" "$STAGE/root/DEBIAN/postrm"
for s in postinst postrm; do sh -n "$STAGE/root/DEBIAN/$s"; done

mkdir -p "$OUTDIR"
DEB="$OUTDIR/pdn-lin_${VERSION}_${ARCH}.deb"
# -Zxz: dpkg-deb's zstd default cannot be unpacked by Debian 11's dpkg.
dpkg-deb --build --root-owner-group -Zxz "$STAGE/root" "$DEB"
echo "built: $DEB"

# The same payload as a tarball: unpack anywhere and run ./pdn-lin. The udev and WirePlumber
# rules travel with it for the operator to install by hand.
TARDIR="$STAGE/pdn-lin-$VERSION"
mkdir -p "$TARDIR/rules"
cp -a "$STAGE/root$PKGDIR/." "$TARDIR/"
ln -s "$EXENAME" "$TARDIR/pdn-lin"
cp "$HERE/70-pdn-lin.rules" "$HERE/50-pdn-lin.conf" "$HERE/51-pdn-lin.lua" "$TARDIR/rules/"
cp "$HERE/copyright" "$TARDIR/COPYRIGHT"
TAR="$OUTDIR/pdn-lin-$VERSION-linux-$TARCH.tar.gz"
tar --sort=name --owner=0 --group=0 --numeric-owner ${SOURCE_DATE_EPOCH:+--mtime=@$SOURCE_DATE_EPOCH} \
  -C "$STAGE" -czf "$TAR" "pdn-lin-$VERSION"
echo "built: $TAR"
