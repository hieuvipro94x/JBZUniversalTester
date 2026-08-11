#!/usr/bin/env bash
set -euo pipefail
APP_DIR="$(cd "$(dirname "$0")" && pwd)"
DESKTOP="/home/sa/Desktop/JBZ Universal Tester.desktop"
cat > "$DESKTOP" <<EOF
[Desktop Entry]
Type=Application
Name=JBZ Universal Tester
Comment=Universal Tester Production
Exec=$APP_DIR/run_gui.sh
Path=$APP_DIR
Terminal=false
Categories=Utility;
EOF
chmod +x "$DESKTOP"
echo "Đã tạo: $DESKTOP"
