# Squeezelite (built from vendored source)

- Source: `src/` is upstream https://github.com/ralph-irving/squeezelite at commit `cc7ee7c` (version 2.0.0-1612), GPLv3 (`src/LICENSE.txt`, `LICENSE`).
- WinSqueeze changes (all marked "WinSqueeze" in the code):
  - `src/output_vis_win.c` (new): Windows visualiser export for `-v`, a shared-memory ring `Local\squeezelite-vis-<mac>` read by the app's meters.
  - `src/squeezelite.h`, `src/output_vis.c`: enable `VISEXPORT` on Windows using that file.
  - `src/output_rec_win.c` (new): radio recorder, shared memory `Local\squeezelite-rec-<mac>`; the output thread copies the PCM it plays (before EQ) into a ring that a writer thread encodes to FLAC. Lists `WINREC` in the build options.
  - `src/output_pa.c`: the process joins COM's multithreaded apartment before `Pa_Initialize`, so every thread that opens the device has COM (the monitor thread opens it on first play with `-C` and after each idle close, and failed with CO_E_NOTINITIALIZED). Failed opens log the Windows HRESULT behind PortAudio's "Invalid device". Exclusive mode uses WASAPI polling, not event mode: event mode writes one fixed chunk per driver wake-up, and some USB DACs (Schiit Modi) then played a chunk and a gap of the same length.
  - `src/main.c`, `src/output_pa.c`: `-F` fixed full-level output (server volume ignored; the app drives the device's hardware volume instead).
  - `src/main.c`, `src/output_pa.c`, `src/output_eq_win.c`: `-H` start held: the output plays silence until the app clears `hold` in the EQ shared memory, so Lyrion restarting playback on connect isn't heard unless the app lets it. Lists `WINHOLD` in the build options.
- Build: `.github/workflows/squeezelite.yml` builds with CMake (`CMakeLists.txt`) and vcpkg static libraries (`vcpkg.json`: PortAudio, FLAC, MAD, mpg123, Ogg/Vorbis, FAAD2, SoX resampler, Opus, OpenSSL, FFmpeg), statically linked with /MT so no VC++ runtime DLL is needed. The workflow commits the resulting `squeezelite-ffmpeg-x64.exe` here.
- The binary reports `Squeezelite 2.0.0-1612-winsqueeze` and lists `VISEXPORT` in its build options.
