#!/usr/bin/env bash
set -u

echo "=== RASPBERRY PI ==="
tr -d '\0' < /proc/device-tree/model 2>/dev/null || true
echo
uname -a
getconf LONG_BIT 2>/dev/null || true

echo
echo "=== CỔNG SERIAL ==="
ls -l /dev/serial* /dev/ttyAMA* /dev/ttyS* /dev/ttyUSB* /dev/ttyACM* 2>/dev/null || echo "Không thấy cổng serial"

echo
echo "=== QUYỀN NGƯỜI DÙNG ==="
id

echo
echo "=== UART TRONG BOOT CONFIG ==="
grep -nEi 'enable_uart|dtoverlay=.*uart|console=serial' /boot/config.txt /boot/firmware/config.txt /boot/cmdline.txt /boot/firmware/cmdline.txt 2>/dev/null || true
