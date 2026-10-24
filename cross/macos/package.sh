#!/bin/sh
# package.sh - publishes CompxTray for macOS, wraps it in CompxTray.app and zips it with the command line tool.
# Runs on macOS (needs sips, iconutil, codesign and ditto). Usage (from the repository root):
#   sh cross/macos/package.sh osx-arm64 1.1.0
set -eu
rid="$1"; version="${2#v}"
dist="out/CompxTray-$rid"
app="$dist/CompxTray.app"

dotnet publish cross/App -c Release -r "$rid" --self-contained -p:Version="$version" -o "out/app-$rid"
dotnet publish cross/Cli -c Release -r "$rid" --self-contained -p:PublishSingleFile=true \
    -p:Version="$version" -o "out/cli-$rid"

rm -rf "$dist"; mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "out/app-$rid/." "$app/Contents/MacOS/"
rm -f "$app/Contents/MacOS/"*.pdb
sed "s/@VERSION@/$version/g" cross/macos/Info.plist > "$app/Contents/Info.plist"

iconset="out/CompxTray.iconset"
rm -rf "$iconset"; mkdir -p "$iconset"
for s in 16 32 128 256; do
    sips -z "$s" "$s" assets/icon.png --out "$iconset/icon_${s}x${s}.png" >/dev/null
    d=$((s * 2))
    sips -z "$d" "$d" assets/icon.png --out "$iconset/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/CompxTray.icns"

cp "out/cli-$rid/compxtray-cli" LICENSE cross/THIRD-PARTY-NOTICES.txt "$dist/"
cp cross/THIRD-PARTY-NOTICES.txt "$app/Contents/Resources/"
# Ad-hoc signatures: required to run on Apple Silicon. Not notarized, so Gatekeeper still asks the first time.
codesign --force --deep --sign - "$app"
codesign --force --sign - "$dist/compxtray-cli"

(cd out && ditto -c -k --keepParent "CompxTray-$rid" "CompxTray-$rid.zip")
(cd out && shasum -a 256 "CompxTray-$rid.zip" > "CompxTray-$rid.zip.sha256")
echo "Built out/CompxTray-$rid.zip"
