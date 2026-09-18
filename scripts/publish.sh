#!/usr/bin/env sh
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
if [ "$#" -eq 0 ]; then set -- win-x64 linux-x64; fi
for rid in "$@"; do
  dotnet publish "$root/src/GAIP.Desktop/GAIP.Desktop.csproj" -c Release -r "$rid" --self-contained true -p:PublishSingleFile=false -o "$root/artifacts/$rid"
done
