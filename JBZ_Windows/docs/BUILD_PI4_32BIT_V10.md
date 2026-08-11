# Build V10 trên Raspberry Pi 4 – Raspberry Pi OS 32-bit

## 1. Kiểm tra kiến trúc

```bash
uname -m
```

Cần thấy:

```text
armv7l
```

```bash
getconf LONG_BIT
```

Cần thấy:

```text
32
```

## 2. Kiểm tra Python

```bash
python3 --version
```

Yêu cầu Python 3.10 trở lên. Khuyến nghị Raspberry Pi OS Bookworm 32-bit với Python 3.11.

## 3. Cài source

```bash
cd /home/sa/Desktop
```

```bash
unzip -o /home/sa/Downloads/JBZUniversalTester_Production_VI_V10.zip
```

```bash
cd /home/sa/Desktop/JBZUniversalTester_Production_VI_V10
```

## 4. Build

```bash
chmod +x build_native.sh
```

```bash
./build_native.sh
```

## 5. Kết quả

```bash
ls -lh release
```

Tên gói có `pi4_arm32`.

```bash
file dist/JBZUniversalTester/JBZUniversalTester
```

Kết quả phải chứa:

```text
ELF 32-bit
ARM
```

## 6. Chạy thử

```bash
cd dist/JBZUniversalTester
```

```bash
./run.sh
```

## 7. Không dùng nhầm bản

- `arm32` chỉ dùng cho Pi OS 32-bit.
- `arm64` chỉ dùng cho Pi OS 64-bit.
- Nên build trên chính máy đích để tránh lỗi GLIBC.
