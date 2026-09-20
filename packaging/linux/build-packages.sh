#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 || $# -gt 3 ]]; then
  echo "Usage: $0 <version> <source-dir> [dist-dir]" >&2
  exit 2
fi

VERSION="$1"
SOURCE_DIR="$2"
DIST_DIR="${3:-dist}"

if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Version invalide: $VERSION (attendu MAJOR.MINOR.PATCH)" >&2
  exit 2
fi

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH= cd -- "$SCRIPT_DIR/../.." && pwd)"
SOURCE_DIR="$(cd "$SOURCE_DIR" && pwd)"
mkdir -p "$DIST_DIR"
DIST_DIR="$(cd "$DIST_DIR" && pwd)"

if [[ ! -x "$SOURCE_DIR/GAIP" ]]; then
  echo "Exécutable Linux introuvable ou non exécutable: $SOURCE_DIR/GAIP" >&2
  exit 1
fi

command -v dpkg-deb >/dev/null 2>&1 || {
  echo "dpkg-deb est requis pour construire le paquet DEB." >&2
  exit 1
}

command -v rpmbuild >/dev/null 2>&1 || {
  echo "rpmbuild est requis pour construire le paquet RPM." >&2
  exit 1
}

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

prepare_payload() {
  local root="$1"

  install -d     "$root/usr/lib/gaip"     "$root/usr/bin"     "$root/usr/share/applications"

  install -m 0755 "$SOURCE_DIR/GAIP" "$root/usr/lib/gaip/GAIP"
  install -m 0755 "$SCRIPT_DIR/gaip" "$root/usr/bin/gaip"
  install -m 0644 "$SCRIPT_DIR/gaip.desktop" "$root/usr/share/applications/gaip.desktop"

  if [[ -d "$SOURCE_DIR/icons" ]]; then
    while IFS= read -r -d '' icon; do
      size="$(basename "$(dirname "$icon")")"
      target="$root/usr/share/icons/hicolor/$size/apps"
      install -d "$target"
      install -m 0644 "$icon" "$target/GAIP.png"
    done < <(find "$SOURCE_DIR/icons" -type f -name 'GAIP.png' -print0)
  fi
}

# Debian / Ubuntu package
DEB_ROOT="$WORK_DIR/deb"
prepare_payload "$DEB_ROOT"
install -d "$DEB_ROOT/DEBIAN"

INSTALLED_SIZE="$(du -sk "$DEB_ROOT/usr" | awk '{print $1}')"
cat > "$DEB_ROOT/DEBIAN/control" <<EOF
Package: gaip
Version: $VERSION
Section: net
Priority: optional
Architecture: amd64
Maintainer: LagunaPower <80133998+LagunaPower@users.noreply.github.com>
Homepage: https://github.com/LagunaPower/gaip
Installed-Size: $INSTALLED_SIZE
Depends: ca-certificates, libc6, libgcc-s1 | libgcc1, libgssapi-krb5-2, libstdc++6, libssl3 | libssl3t64, libicu72 | libicu74 | libicu76 | libicu78, tzdata, zlib1g, libx11-6, libice6, libsm6, libfontconfig1, libwayland-client0, libxkbcommon0, libegl1, libgl1
Description: Gestion d'Adresses IP
 G@IP est une application desktop de gestion de sites, VLAN, sous-réseaux
 IPv4, passerelles et adresses IP attribuées.
EOF

cat > "$DEB_ROOT/DEBIAN/postinst" <<'EOF'
#!/usr/bin/env sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database /usr/share/applications >/dev/null 2>&1 || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -f -t /usr/share/icons/hicolor >/dev/null 2>&1 || true
fi
exit 0
EOF
chmod 0755 "$DEB_ROOT/DEBIAN/postinst"

cat > "$DEB_ROOT/DEBIAN/postrm" <<'EOF'
#!/usr/bin/env sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database /usr/share/applications >/dev/null 2>&1 || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -f -t /usr/share/icons/hicolor >/dev/null 2>&1 || true
fi
exit 0
EOF
chmod 0755 "$DEB_ROOT/DEBIAN/postrm"

DEB_OUT="$DIST_DIR/gaip_${VERSION}_amd64.deb"
dpkg-deb --root-owner-group --build "$DEB_ROOT" "$DEB_OUT"

# RPM package
RPM_TOP="$WORK_DIR/rpmbuild"
mkdir -p "$RPM_TOP"/{BUILD,BUILDROOT,RPMS,SOURCES,SPECS,SRPMS}

RPM_SOURCE_ROOT="$WORK_DIR/source/gaip-$VERSION"
mkdir -p "$RPM_SOURCE_ROOT"
prepare_payload "$RPM_SOURCE_ROOT"

tar -C "$WORK_DIR/source" -czf "$RPM_TOP/SOURCES/gaip-$VERSION.tar.gz" "gaip-$VERSION"

SPEC="$RPM_TOP/SPECS/gaip.spec"
cat > "$SPEC" <<EOF
# Binaire .NET single-file : ne jamais le passer à strip (perte du bundle).
%global debug_package %{nil}
%global __strip /bin/true
%global __os_install_post %{nil}
%global _build_id_links none

Name:           gaip
Version:        $VERSION
Release:        1
Summary:        Gestion d'Adresses IP
License:        MIT
URL:            https://github.com/LagunaPower/gaip
Source0:        %{name}-%{version}.tar.gz
BuildArch:      x86_64

Requires:       glibc
Requires:       libgcc
Requires:       ca-certificates
Requires:       openssl-libs
Requires:       libstdc++
Requires:       libicu
Requires:       tzdata
Requires:       krb5-libs
Requires:       libX11.so.6()(64bit)
Requires:       libICE.so.6()(64bit)
Requires:       libSM.so.6()(64bit)
Requires:       libfontconfig.so.1()(64bit)
Requires:       libxkbcommon.so.0()(64bit)
Requires:       libwayland-client.so.0()(64bit)
Requires:       libEGL.so.1()(64bit)
Requires:       libGL.so.1()(64bit)

%description
G@IP est une application desktop de gestion de sites, VLAN, sous-réseaux
IPv4, passerelles et adresses IP attribuées.

%prep
%setup -q

%build

%install
rm -rf %{buildroot}
mkdir -p %{buildroot}
cp -a usr %{buildroot}/

%post
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database /usr/share/applications >/dev/null 2>&1 || :
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -f -t /usr/share/icons/hicolor >/dev/null 2>&1 || :
fi

%postun
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database /usr/share/applications >/dev/null 2>&1 || :
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
  gtk-update-icon-cache -f -t /usr/share/icons/hicolor >/dev/null 2>&1 || :
fi

%files
%license
/usr/lib/gaip/GAIP
/usr/bin/gaip
/usr/share/applications/gaip.desktop
/usr/share/icons/hicolor/*/apps/GAIP.png

%changelog
* Sat Sep 19 2026 LagunaPower <80133998+LagunaPower@users.noreply.github.com> - $VERSION-1
- Automated G@IP package build
EOF

# rpmbuild's %license directive requires a packaged license file. The project
# license is copied into the source tree before the source archive is rebuilt.
install -m 0644 "$REPO_ROOT/LICENSE" "$RPM_SOURCE_ROOT/LICENSE"
tar -C "$WORK_DIR/source" -czf "$RPM_TOP/SOURCES/gaip-$VERSION.tar.gz" "gaip-$VERSION"

# Add the license path now that it is part of Source0.
python3 - "$SPEC" <<'PY'
from pathlib import Path
import sys
path = Path(sys.argv[1])
text = path.read_text()
text = text.replace("%license\n/usr/lib/gaip/GAIP", "%license LICENSE\n/usr/lib/gaip/GAIP")
path.write_text(text)
PY

rpmbuild --define "_topdir $RPM_TOP" -bb "$SPEC"

RPM_BUILT="$(find "$RPM_TOP/RPMS" -type f -name "gaip-$VERSION-1.x86_64.rpm" -print -quit)"
if [[ -z "$RPM_BUILT" ]]; then
  echo "Paquet RPM construit introuvable." >&2
  exit 1
fi

cp "$RPM_BUILT" "$DIST_DIR/gaip-$VERSION-1.x86_64.rpm"

echo "Paquets créés :"
echo "  $DEB_OUT"
echo "  $DIST_DIR/gaip-$VERSION-1.x86_64.rpm"
