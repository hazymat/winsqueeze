# Squeezelite (built from vendored source)

- Source: `src/` is upstream https://github.com/ralph-irving/squeezelite at commit `cc7ee7c` (version 2.0.0-1612), GPLv3 (`src/LICENSE.txt`, `LICENSE`).
- WinSqueeze changes (all marked "WinSqueeze" in the code):
  - `src/output_vis_win.c` (new): Windows visualiser export for `-v`, a shared-memory ring `Local\squeezelite-vis-<mac>` read by the app's meters.
  - `src/squeezelite.h`, `src/output_vis.c`: enable `VISEXPORT` on Windows using that file.
  - `src/main.c`, `src/output_pa.c`: `-F` fixed full-level output (server volume ignored; the app drives the device's hardware volume instead).
- Build: `.github/workflows/squeezelite.yml` builds with CMake (`CMakeLists.txt`) and vcpkg static libraries (`vcpkg.json`: PortAudio, FLAC, MAD, mpg123, Ogg/Vorbis, FAAD2, SoX resampler, Opus, OpenSSL, FFmpeg), statically linked with /MT so no VC++ runtime DLL is needed. The workflow commits the resulting `squeezelite-ffmpeg-x64.exe` here.
- The binary reports `Squeezelite 2.0.0-1612-winsqueeze` and lists `VISEXPORT` in its build options.
