#!/usr/bin/env sh
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
profile=Release
if [ "${1:-}" = "--profile" ]; then profile=${2:?Profil manquant}; shift 2; fi
case "$profile" in Development) configuration=Debug ;; Release) configuration=Release ;; *) printf 'Profil inconnu : %s\n' "$profile" >&2; exit 1 ;; esac
if [ "$#" -eq 0 ]; then set -- win-x64 linux-x64; fi

verify_single_file() {
  output=$1
  label=$2
  if [ -n "$(find "$output" -type f \( -name '*.pdb' -o -name '*.dll' -o -name '*.so' -o -name '*.deps.json' -o -name '*.runtimeconfig.json' \) -print)" ]; then
    printf 'Fichiers séparés inattendus dans la publication %s : %s\n' "$label" "$output" >&2
    exit 1
  fi
}

for rid in "$@"; do
  dotnet publish "$root/src/GAIP.Desktop/GAIP.Desktop.csproj" -c "$configuration" -r "$rid" -p:PublishProfile="$profile"

  if [ "$profile" = Release ]; then
    verify_single_file "$root/artifacts/Release/$rid" "Release $rid"
  fi
done
