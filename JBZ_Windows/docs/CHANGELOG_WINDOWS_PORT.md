# Windows portability changes

- Added `jbz_platform.py` for Windows/Linux path resolution.
- Production config/database/log paths now use APPDATA/LOCALAPPDATA on Windows.
- Model/Setup defaults map to `%USERPROFILE%\Models` and `%USERPROFILE%\Setups`.
- UART discovery now supports Windows COM ports via `serial.tools.list_ports`.
- Fixed cached COM handling: no `os.path.exists(COMx)` check on Windows.
- Preserved UART handshake, protocol, timing, model compiler, fault logic and GUI structure.
- Added Windows install, run, build and UART diagnostic scripts.
