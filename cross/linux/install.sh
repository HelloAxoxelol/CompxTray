#!/bin/sh
# install.sh - installs CompxTray for the current user, plus the udev rule that gives it access to the receiver.
# Run from the unpacked folder:  ./install.sh
set -eu
here="$(cd "$(dirname "$0")" && pwd)"
bin="$HOME/.local/bin"
data="${XDG_DATA_HOME:-$HOME/.local/share}"

mkdir -p "$bin" "$data/applications" "$data/icons/hicolor/256x256/apps"
install -m 755 "$here/CompxTray" "$bin/CompxTray"
install -m 755 "$here/compxtray-cli" "$bin/compxtray-cli"
install -m 644 "$here/compxtray.png" "$data/icons/hicolor/256x256/apps/compxtray.png"
sed "s|@BIN@|$bin|" "$here/compxtray.desktop" > "$data/applications/compxtray.desktop"
echo "Installed CompxTray and compxtray-cli in $bin"

echo "Installing the udev rule in /etc/udev/rules.d (asks for your password)..."
sudo install -m 644 "$here/70-compxtray.rules" /etc/udev/rules.d/70-compxtray.rules
sudo udevadm control --reload-rules
sudo udevadm trigger --subsystem-match=hidraw
echo "Done. Unplug and replug the receiver, then start CompxTray from your applications menu."
