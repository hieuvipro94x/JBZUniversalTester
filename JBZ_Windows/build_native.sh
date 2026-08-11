#!/usr/bin/env bash
set -Eeuo pipefail
cd "$(dirname "$0")"

APP_NAME="JBZUniversalTester"
APP_VERSION="$(tr -d '[:space:]' < VERSION.txt 2>/dev/null || true)"
APP_VERSION="${APP_VERSION:-V10}"
ARCH_RAW="$(uname -m)"

case "$ARCH_RAW" in
    armv6l|armv7l) ARCH_TAG="arm32" ;;
    aarch64|arm64) ARCH_TAG="arm64" ;;
    *) echo "Không hỗ trợ kiến trúc build: $ARCH_RAW"; exit 1 ;;
esac

. /etc/os-release
OS_TAG="${VERSION_CODENAME:-${VERSION_ID:-unknown}}"
PI_MODEL="$(tr -d '\0' < /proc/device-tree/model 2>/dev/null || echo Raspberry_Pi)"
case "$PI_MODEL" in
    *"Raspberry Pi 5"*) PI_TAG="pi5" ;;
    *"Raspberry Pi 4"*) PI_TAG="pi4" ;;
    *) PI_TAG="pi" ;;
esac
PY_TAG="$(python3 -c 'import sys; print(f"py{sys.version_info.major}{sys.version_info.minor}")')"
PACKAGE="${APP_NAME}_${APP_VERSION}_${PI_TAG}_${ARCH_TAG}_${OS_TAG}_${PY_TAG}"

printf 'Máy: %s\nKiến trúc: %s\nGói: %s\n' "$PI_MODEL" "$ARCH_RAW" "$PACKAGE"

sudo apt update
sudo apt install -y \
    python3 python3-venv python3-pip python3-tk python3-dev python3-serial \
    build-essential binutils patchelf fonts-noto-cjk

rm -rf .build-venv build dist release "${APP_NAME}.spec"
python3 -m venv --system-site-packages .build-venv
.build-venv/bin/python -m pip install --upgrade pip setuptools wheel
.build-venv/bin/python -m pip install -r requirements.txt
.build-venv/bin/python -m pip install 'pyinstaller>=6,<7'

.build-venv/bin/python -m PyInstaller \
    --noconfirm \
    --clean \
    --onedir \
    --windowed \
    --name "$APP_NAME" \
    --paths . \
    --collect-submodules jbz_tester \
    --collect-submodules jbz_model_loader \
    --collect-submodules jbz_uart \
    --hidden-import serial.tools.list_ports \
    app.py

mkdir -p "dist/$APP_NAME/docs" "dist/$APP_NAME/profiles"
cp -a docs/. "dist/$APP_NAME/docs/" 2>/dev/null || true
cp -a profiles/. "dist/$APP_NAME/profiles/" 2>/dev/null || true
cp -f README.md README_VI.md VERSION.txt "dist/$APP_NAME/" 2>/dev/null || true

cat > "dist/$APP_NAME/run.sh" <<'RUN'
#!/usr/bin/env bash
set -Eeuo pipefail
cd "$(dirname "$0")"
exec ./JBZUniversalTester
RUN
chmod +x "dist/$APP_NAME/run.sh" "dist/$APP_NAME/$APP_NAME"

mkdir -p release
tar -C dist -czf "release/${PACKAGE}.tar.gz" "$APP_NAME"
sha256sum "release/${PACKAGE}.tar.gz" > "release/${PACKAGE}.tar.gz.sha256"

file "dist/$APP_NAME/$APP_NAME"
printf '\nBUILD THÀNH CÔNG:\n%s\n' "$PWD/release/${PACKAGE}.tar.gz"
cat "release/${PACKAGE}.tar.gz.sha256"
