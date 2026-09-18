#!/usr/bin/env sh
# Register this portable distribution in the current user's desktop menu.
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
data=${XDG_DATA_HOME:-"$HOME/.local/share"}
mkdir -p "$data/applications"
for directory in "$root"/icons/*x*; do
  size=$(basename "$directory")
  target="$data/icons/hicolor/$size/apps"
  mkdir -p "$target"
  cp "$directory/GAIP.png" "$target/GAIP.png"
done
# Desktop Entry quoted Exec syntax; a literal percent must be doubled.
escaped=$(printf '%s' "$root/GAIP" | sed 's/\\/\\\\/g; s/"/\\"/g; s/`/\\`/g; s/\$/\\$/g; s/%/%%/g')
{
  printf '%s\n' '[Desktop Entry]' 'Type=Application' 'Name=G@IP' 'GenericName=Gestion d’Adresses IP'
  printf 'Exec="%s"\n' "$escaped"
  printf '%s\n' 'Icon=GAIP' 'Terminal=false' 'Categories=Network;Utility;' 'StartupWMClass=GAIP'
} > "$data/applications/GAIP.desktop"
chmod +x "$root/GAIP"
if command -v gtk-update-icon-cache >/dev/null 2>&1; then gtk-update-icon-cache -f -t "$data/icons/hicolor" >/dev/null 2>&1 || true; fi
printf 'G@IP ajouté au menu des applications depuis %s\n' "$root"
