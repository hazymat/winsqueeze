# Phase 1: WASAPI exclusive mode check

Tested 5 Nov 2026 on flanders with the AudioQuest DragonFly Black v1.5 against Lyrion at 10.1.1.44, using the pinned `squeezelite-ffmpeg-x64.exe` 2.0.0-1610.

## Binary

- Exclusive mode is built in: `-a <latency_ms>:<exclusive 0|1>` (Windows PortAudio build, `paWinWasapiExclusive`).
- Output device is matched by prefix on `"<name> [<host API>]"`, so name it as `"Speakers (AudioQuest DragonFly Black v1.5) [Windows WASAPI]"`, never by index (index was 10 on flanders).
- Needs `VCRUNTIME140.dll` (VC++ 2015–2022 x64 runtime). The installer must ship it.
- No visualiser/level export on Windows (`VISEXPORT` is Linux/macOS only). Meters need a small patch (Phase 5).

## Results

| Run | Result |
| --- | --- |
| Exclusive, `-a 40:1` | DAC locks at 44.1 kHz (green LED) but the stream stops and reopens about every 10 s; no audible output |
| Exclusive, `-a 200:1` | Plays cleanly at 44.1 kHz, 16- and 24-bit FLAC; heard by Mat, LED green |
| Exclusive, `-a 200:1`, 96/24 test tone | DAC switches to 96 kHz (LED magenta), tone heard by Mat |
| Shared, `-a 40:0` | Fails: "Invalid sample rate" repeatedly. Squeezelite doesn't resample, so shared mode only works when the track rate matches the Windows mixer rate |

- In exclusive mode the DragonFly reports 44100, 48000, 88200 and 96000. Squeezelite tells the server `MaxSampleRate=96000`.
- 96 kHz: verified with a 30 s 96/24 WAV test tone (440 Hz L / 660 Hz R) added to the library as `_WinSqueeze test`. The library itself has nothing above 48 kHz.
- Log: `track start sample rate: 96000` → `opening WASAPI device in exclusive mode` → `opened device 10 - Speakers (AudioQuest DragonFly Black v1.5) [Windows WASAPI] at 96000 latency 210 ms`.
- The server sent the WAV test file as FLAC (its default lossless convert rule), which is still bit-perfect. The fidelity check in the shell should report this as "lossless transcode" rather than warn.
- **Verdict: the pinned build supports WASAPI exclusive with the DragonFly at 44.1 and 96 kHz. No custom Squeezelite build needed.**

## Decisions for the shell

- Hardware volume: the Windows master (endpoint) volume still drives the DragonFly's hardware volume in exclusive mode, while the data stays bit-perfect. There is no per-app mixer slider. WinSqueeze's volume control should drive the endpoint volume when the device has hardware volume, keeping Squeezelite at 100.

- Default Squeezelite args: `-a 200:1`, device by full name + host API.
- Shared-mode fallback needs resampling (`-R`) or the mixer rate, and must be clearly labelled as not bit-perfect.
- Read-only player prefs seen: `digitalVolumeControl` 1 (server-side digital volume is on; WinSqueeze must warn and, for fixed volume, keep player volume at 100).
