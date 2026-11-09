# WinSqueeze: working notes

Read `SPEC.md` before any work. It is the source of truth; propose changes to it rather than silently diverging.

## Dev environment

- Build and test on Windows (the app can't be built or tested on Linux). The test PC, server address and library details live in the project's private notes, not in this public repo.
- Squeezelite: Windows x64 FFmpeg build `squeezelite-2.0.0-1610-ffmpeg-win64` from SourceForge (see `vendor/squeezelite/SOURCE.md`).
- Test DAC: AudioQuest DragonFly (USB).

## Git identity (required)

Never credit Claude/Anthropic anywhere: no `Co-Authored-By` trailers, no "Generated with" lines, no session links, no Claude author. Cloud containers default to `user.name Claude` / `noreply@anthropic.com` in `/root/.gitconfig`, and GitHub's squash merge turns that author into a `Co-authored-by: Claude` trailer on main, which puts Claude in the contributors list. Before the first commit in any clone run:

```
git config user.name hazymat && git config user.email 7063284+hazymat@users.noreply.github.com
```

The `No AI attribution` workflow fails any PR whose commits credit Claude; fix the commits, don't merge past it.

## How to work

Work one build phase at a time (SPEC.md › Build phases). Stop at the end of each phase for review. Commit in small, described steps.

## Code style

- Compact code, descriptive variable names (no single-letter names), concise useful comments only.
- No spread operator. Never use CSS `!important`.

## Translations

- UI text goes through `t('…')` / `tn(count, '…', '…')` (Svelte) or `L.T("…")` / `L.N(count, "…", "…")` (C#); fixed XAML text is translated by `L.Translate`. Keep the English literal inside the call so `tools/locales.mjs` can find it.
- Every new or changed string needs its German entry in `locales/de.json` (informal "du"). Run `node tools/locales.mjs` to list what's missing.

## Layout

- `/app`: C# .NET 8 WPF shell hosting WebView2.
- `/ui`: TypeScript + Vite + Svelte front end, built into `/app/wwwroot`.
- `/vendor/squeezelite`: pinned Squeezelite binary, its `LICENSE` and `SOURCE.md`.
- `/docs`: notes and screenshots.
