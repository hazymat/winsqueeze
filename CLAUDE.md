# WinSqueeze: working notes

Read `SPEC.md` before any work. It is the source of truth; propose changes to it rather than silently diverging.

## Dev environment

- Build and test on Windows (the app can't be built or tested on Linux). Mat's Windows machine is **flanders**; the DragonFly is plugged into it.
- Test server: Lyrion Music Server at `http://10.1.1.44:9000`.
- Squeezelite: Windows x64 FFmpeg build `squeezelite-2.0.0-1610-ffmpeg-win64` from SourceForge (see `vendor/squeezelite/SOURCE.md`).
- Test DAC: AudioQuest DragonFly (USB).
- Test library: about 900 albums, 9,600 tracks, 650 artists; a mix of jazz and classical; many folders map one-to-one to a CD.

## How to work

Work one build phase at a time (SPEC.md › Build phases). Stop at the end of each phase for review. Commit in small, described steps.

## Code style

- Compact code, descriptive variable names (no single-letter names), concise useful comments only.
- No spread operator. Never use CSS `!important`.

## Layout

- `/app`: C# .NET 8 WPF shell hosting WebView2.
- `/ui`: TypeScript + Vite + Svelte front end, built into `/app/wwwroot`.
- `/vendor/squeezelite`: pinned Squeezelite binary, its `LICENSE` and `SOURCE.md`.
- `/docs`: notes and screenshots.
