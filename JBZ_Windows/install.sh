#!/usr/bin/env bash
set -Eeuo pipefail
cd "$(dirname "$0")"

sudo apt update
sudo apt install -y \
    python3 \
    python3-venv \
    python3-tk \
    python3-serial \
    fonts-noto-cjk

rm -rf .venv
python3 -m venv --system-site-packages .venv

if ! .venv/bin/python -c 'import serial; import tkinter' >/dev/null 2>&1; then
    .venv/bin/python -m pip install -r requirements.txt
fi

mkdir -p /home/sa/Models /home/sa/Setups

if getent group dialout >/dev/null 2>&1; then
    sudo usermod -aG dialout "${USER}"
fi

chmod +x run_gui.sh install_desktop.sh build_native.sh tools/check_uart_devices.sh

printf '\nCài đặt V10 hoàn tất.\n'
printf 'Phần mềm sẽ tự quét và kết nối đúng bo Universal Tester.\n'
printf 'Chạy: ./run_gui.sh\n\n'
printf 'Các cổng serial hiện có:\n'
ls -l /dev/serial* /dev/ttyAMA* /dev/ttyS* /dev/ttyUSB* /dev/ttyACM* 2>/dev/null || true
printf '\nNếu tài khoản vừa được thêm vào nhóm dialout, hãy khởi động lại Pi một lần.\n'
