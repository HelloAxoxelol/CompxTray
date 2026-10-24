#!/bin/sh
# package.sh - publishes CompxTray for Linux and packs it with the udev rule and the installer.
# Usage (from the repository root):  sh cross/linux/package.sh linux-x64 1.1.0
set -eu
rid="$1"; version="${2#v}"
dist="out/CompxTray-$rid"

dotnet publish cross/App -c Release -r "$rid" --self-contained -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true -p:Version="$version" -o "out/app-$rid"
dotnet publish cross/Cli -c Release -r "$rid" --self-contained -p:PublishSingleFile=true \
    -p:Version="$version" -o "out/cli-$rid"

rm -rf "$dist"; mkdir -p "$dist"
cp "out/app-$rid/CompxTray" "out/cli-$rid/compxtray-cli" "$dist/"
cp cross/linux/install.sh cross/linux/70-compxtray.rules cross/linux/compxtray.desktop "$dist/"
cp LICENSE cross/THIRD-PARTY-NOTICES.txt "$dist/"
cp assets/icon.png "$dist/compxtray.png"
chmod 755 "$dist/CompxTray" "$dist/compxtray-cli" "$dist/install.sh"

tar -C out -czf "out/CompxTray-$rid.tar.gz" "CompxTray-$rid"
(cd out && sha256sum "CompxTray-$rid.tar.gz" > "CompxTray-$rid.tar.gz.sha256")
echo "Built out/CompxTray-$rid.tar.gz"
