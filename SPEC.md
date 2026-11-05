# WinSqueeze: Specification

## Overview

WinSqueeze is a standalone Windows app that browses, controls and plays music from any Lyrion Music Server without touching the server. You launch it, browse your library, and it plays bit-perfect through your chosen output. You never open the server's web UI.

**A synchronising client, which is what makes it different.** WinSqueeze keeps a complete local mirror of the server's library (tracks, tags, contributors, folders, cover art) and keeps it in sync automatically. Every Lyrion controller we know of asks the server live for each screen. WinSqueeze browses and searches its own synced copy, so it is instant over any connection, and it can offer views the server doesn't, such as folder-grouped search. The server stays the single source of truth; WinSqueeze only ever follows it.

**USP: fully autonomous of the server.** No server plugins, no skin switching, no server settings changed. WinSqueeze only reads the library through Lyrion's standard JSON-RPC API and controls playback like any other Squeezebox player.

**Who it's for:**

- **Audiophile listeners**, especially of jazz and classical, who want bit-perfect playback and browsing that goes beyond artist and album, such as by composer and by performer, conductor or ensemble.
- **Careful archivists** who have built their collection over many years so that each folder on disk is one CD, LP or album. WinSqueeze treats that folder structure as a first-class view of the library.

**Signature feature: search by tag, grouped by folder.** Search and browse use the ID3/Vorbis tags (composer, performer, genre, artist), but the results are grouped by music folder, so each hit shows up as the physical CD or LP it belongs to. We haven't found another Lyrion controller that does this.

Why it exists:

- Material Skin and the default web UI are cluttered, make browsing the catalogue hard, and hide transport controls at narrow widths.
- Running Squeezelite on Windows means a hidden console window, a hand-made Startup shortcut and Task Manager to stop it.
- Browser playback goes through the Windows mixer and Lyrion's browser stream is MP3, so neither is bit-perfect.
- Browsing over a VPN or slow link is sluggish when every click hits the server.
- Building a playlist while browsing is awkward in every existing controller.

## Goals

1. **Server untouched.** Read-only use of the library API. Never write server settings, prefs or plugin state. The only commands sent are normal player control (play, pause, queue, volume) and favourites or saved playlists the user explicitly asks for.
2. **Self-contained playback.** Squeezelite is bundled and run hidden. Quitting the app stops it cleanly. No console windows, shortcuts or Task Manager.
3. **Local-first.** A full local index of the library and cached cover art. Browsing and search are served from the cache and feel instant.
4. **Slow-link friendly.** Works well over VPN or poor Wi-Fi. Only playback, control and cache deltas cross the network.
5. **Sensible cache refresh.** Detect server rescans and fetch only what changed. Manual Refresh available.
6. **Scale.** Fast from a few hundred albums to 200,000+ tracks. Must work well for other people's libraries, not just the author's (~900 albums, ~9,600 tracks).
7. **Fidelity first.** Lossless end to end, bit-perfect where the device allows, native sample rates, signal path shown on screen.
8. **Native output selection.** Pick the audio output inside the app, remembered by name, hot-plug aware.

## Architecture

One Windows app with three parts: a C# shell, a web front end hosted in WebView2, and a bundled Squeezelite. The server is only ever a data source and a playback service.

```
+---------------- WinSqueeze (your PC) -----------------+
|                                                        |
|  Web UI (WebView2) <--bridge--> C# shell --------------+--- JSON-RPC: library + control ---> Lyrion server
|                                   |     \              |                                     (unchanged)
|                                   |      \ start/stop  |                                          ^
|                     Local cache (SQLite   Squeezelite  +------ lossless audio stream ------------+
|                     index + cover art)    (hidden)     |
+-------------------------------------------|------------+
                                            | WASAPI exclusive
                                            v
                                        USB DAC
```

- **C# shell (.NET 8, WPF):** owns the window, tray, settings, server discovery, the Squeezelite process, and all HTTP calls to Lyrion. The UI never calls the server directly, which avoids CORS and keeps credentials out of the web layer.
- **Web UI:** HTML/CSS/TypeScript in WebView2, served from app resources. Talks to the shell over the WebView2 message bridge. Queries the local index through the shell for all browsing and search.
- **Squeezelite:** pinned FFmpeg build, launched hidden with `-s <server> -n <player name> -o <device>`, restarted on device or server change, stopped on quit. Registers with Lyrion as a normal player.
- **Live state:** current track, queue and transport state come from Lyrion via JSON-RPC polling (about 1 s while visible, slower when hidden) or the CometD subscription if reliable.

## Library model and cache

The app builds and keeps its own SQLite index of the whole library, one database per server profile, in `%LOCALAPPDATA%\WinSqueeze`. All browsing, grouping and search run against it.

**What is fetched (paged, e.g. 2,000 tracks per request):**

- Per track: id, title, track/disc number, duration, URL (gives the folder path), album id, contributors with roles, genres, year, content type, sample rate, bit depth, bitrate.
- Contributors with every role Lyrion knows: artist, album artist, track artist, composer, conductor, band/orchestra.
- Albums, genres, favourites, playlists, cover art (thumbnails on demand, then cached on disk).

**Album source is a switch (key design point).** Two modes, chosen in settings, switchable at any time without re-downloading:

- **Tag albums:** Lyrion's own albums, built from tags.
- **Folder albums:** one folder = one album/CD, built locally by grouping tracks on their parent folder from the track URL. Multi-disc folders (`CD1`, `CD2`) are grouped under the parent folder.

**Contributor grouping is a switch.** For an artist, "contributing" albums can be matched by:

- **Any credit:** the name appears in a multi-name album artist (e.g. "Keith Jarrett, Gary Peacock") or as a track artist, composer, conductor or band.
- **Album artist text only:** the name appears within the album artist field.

Name matching splits on common separators (`,` `;` `&` `/` `feat.` `with` `and`) and is case- and accent-insensitive. The separator list is configurable.

**Refresh:**

- On launch and every few minutes, poll `serverstatus` for last-scan time and library totals. If unchanged, do nothing.
- If changed, fetch a delta (tracks added or modified since the last sync) and remove tracks that no longer exist. Fall back to a full background rebuild if the delta can't be trusted. Browsing keeps working from the old cache meanwhile.
- Manual "Refresh library" and "Rebuild cache" in settings.
- The first build shows a progress bar; the app is usable as soon as the first pages land.

## Browsing

Sidebar sections, in this order: Favourites, Artists, Genres, Composers, Performers, Folders, Queue.

| Section | List shows | Click opens |
| --- | --- | --- |
| Favourites | Lyrion favourites: streams (Radio 3, Radio 4), random mixes (e.g. jazz), albums, artists, playlists | Plays it, or opens it if it's a container |
| Artists | One merged list of every artist and album artist (no separate "album artist" concept) | That artist's albums: their own first, then a separate "Also appears on" group of contributing albums |
| Genres | All genres | Albums in that genre, grouped under album artist headings, with contributing albums folded under the main artist as above |
| Composers | Everyone credited as composer | Albums (CDs) containing at least one work by them, not a list of works |
| Performers | Merged conductors, bands/orchestras/ensembles and soloists, with a role filter chip | Albums they appear on |
| Folders | The music folder tree as stored on disk | Subfolders; a folder of tracks opens as an album |
| Queue | Current player's queue | — |

**Search-as-you-type:**

- Starts after the second character, debounced about 150 ms, served entirely from the local index.
- Scope toggle next to the box: **This list** (default; filters the current view) or **Everything** (results grouped as Artists, Composers, Performers, Albums, Tracks).
- In Folder albums mode, album and track hits are grouped by music folder (the signature feature).
- Accent- and case-insensitive, matching from the start of any word ("jarr" finds Keith Jarrett).
- Typing in an artist search immediately shows that artist's albums underneath the best match.

**Album lists:** cover grid by default, switchable to a compact list, remembered per section. Sorted by album artist then year; sort options for title, year and date added.

## Album view, queue and favourites

**Album view (CD view):** opening an album shows large cover art, album artist, year, format summary (e.g. FLAC 24/96), then the track list grouped by disc. In folder mode, a CD holding works by several composers shows each track's composer in a quiet line under the title.

- Buttons: **Play** (replaces the queue), **Play next**, **Add to queue**, **Favourite** (star).
- Track row: click plays from that track onward; right-click or ⋯ menu has Play next, Add to queue, Go to artist, Go to composer, Open folder.

**Queue:** full view in the sidebar section and inside the expanded now-playing view. Drag to reorder, remove, clear, save as Lyrion playlist. Shows total remaining time.

**Favourites:** star any album, artist, folder, playlist or stream from anywhere; unstar the same way. Changes go to Lyrion's own favourites, so they stay in sync with other controllers. Only on user action.

**Players:** a player switcher in the transport bar. Defaults to this PC's bundled Squeezelite; any other player on the server (other Squeezeboxes in the house) can be picked and controlled the same way.

## Playlist builder

Building a playlist works like opening a case: start it, browse anywhere while it stays open, add things as you go, then close it off. Lyrion and its existing controllers have no comfortable way to do this.

1. **Start:** "New playlist" from the sidebar, the tray menu, or Ctrl+N. Give it a name, or keep the default "Untitled playlist". It can also start from the current queue, or by reopening an existing playlist.
2. **Stays open while you browse:** the playlist docks as a slim panel on the right in Desktop mode, or as a bottom sheet above the transport bar in Vertical mode. It shows the name, track count and running total time. It can be collapsed to a small chip without closing it.
3. **Add from anywhere:** while a playlist is open, every album, track, folder, artist, composer and search result shows a "+ Add to <name>" button next to Play, which changes to "✓ Added" once added. You can also drag items onto the panel. Adding an album or folder adds all its tracks in order. Duplicates are flagged, not blocked. Each add shows a short toast with Undo.
4. **Edit the running total:** expand the panel to reorder by dragging, remove tracks, or undo the last add. Click a track to preview it without losing your place.
5. **Close it off:** "Save & close" saves it as a Lyrion playlist, so other controllers can use it too, with a "Save as favourite" tick box that is on by default. "Discard" throws it away.
6. **Never lost:** an open playlist is autosaved locally as a draft and survives a restart or crash. Only one playlist is open at a time.
7. **Saving is the user's choice:** saving a playlist, like favouriting something, is a deliberate write to the server. Nothing is written until the user saves.

## Transport and now playing

**Bottom bar, always visible in every window mode:** cover art, title, artist, previous / play-pause / next, seek bar with times, volume, player switcher, output device indicator, level meters and the signal readout.

- The bar never drops transport controls at narrow widths. Lower-priority items collapse first, in this order: format readout, player switcher, seek times, volume slider (becomes an icon popover).
- Clicking the art or title opens the **expanded now-playing view**: large art over a blurred-art backdrop, full metadata (composer, performers, album, year), signal path readout, large meters, and the queue alongside.
- **Meters** are driven by the real playback level of the bundled Squeezelite (see Risks). For other players, they show a muted idle state. Two styles, toggled by clicking the meters: analogue **VU needles** or **OLED bar graph**.
- **Signal readout:** format, sample rate, bit depth, and a "bit-perfect" badge when the whole chain is untouched.

## Playback and fidelity

Fidelity is a headline feature, not an afterthought. Playback always goes through the bundled Squeezelite (FFmpeg build), never the browser engine.

1. **Lossless end to end.** The server sends original files in their native format (FLAC, ALAC, WAV, AIFF, hi-res). Squeezelite decodes locally. No MP3 transcoding.
2. **Bypass the Windows mixer.** Default output is the device's WASAPI endpoint in exclusive mode, giving bit-perfect output with no other system sounds mixed in. Falls back to shared mode with a visible warning if exclusive isn't available.
3. **Native sample rates.** 44.1, 48, 88.2, 96, 176.4 and 192 kHz pass straight through; the DAC switches rate per track. No resampling by default.
4. **Volume:** default fixed full-level output, with volume handled at the DAC or amp. Optional Squeezelite digital volume, clearly labelled as not bit-perfect.
5. **Server-side check (read-only).** Read the player's bitrate-limit and transcoding prefs from the server. If anything would force transcoding, show a warning explaining which server setting to change. Never change it.
6. **Gapless** playback preserved (native Squeezelite behaviour).
7. **Signal path on screen:** source format → decoder → output mode → device rate, with the bit-perfect badge.

Bandwidth note: CD-quality FLAC needs about 1–1.5 Mbps, hi-res 24/96 about 3–5 Mbps; fine for typical VPN links.

## Output device selection

1. **Picker in the app:** a dropdown in the transport bar's device indicator, in settings (Audio tab) and in the tray menu.
2. **Friendly names, grouped by driver:** WASAPI, ASIO (if present), DirectSound/MME. Each shows whether it supports exclusive mode.
3. **One-click switch:** restarts the hidden Squeezelite on the new device; if it was playing, playback resumes at the same position.
4. **Remembered by name and driver, never by index.** Windows device numbers change when hardware is plugged in.
5. **Missing device at startup:** show a clear warning and either wait for it to return or use a user-chosen fallback. Never silently play through laptop speakers.
6. **Hot-plug:** device list refreshes automatically when a USB DAC is connected or removed; if the chosen device returns, switch back to it.

## Window modes and layout

One resizable window that changes layout by its own size. Breakpoints are in device-independent pixels, so they scale with Windows display scaling.

| Mode | Triggered when | Layout |
| --- | --- | --- |
| Desktop | Wider than ~900 | Left sidebar, main browse area, bottom transport bar |
| Vertical | ~420–900 wide, taller than wide | Sidebar becomes a bottom tab bar above the transport bar; single-column browsing |
| Mini | Smaller than ~420 × ~260 | Winamp-style strip, roughly 2:1: small art, title, artist, prev/play/next, small meters, seek line. No browsing |

- Modes switch automatically as the window is resized, and Ctrl+1/2/3 jumps straight to a mode (restoring the size last used for it).
- Mini mode has an **always-on-top** pin. It floats freely; no edge snapping.
- **Multi-monitor:** size, position and mode are remembered per monitor configuration (keyed by the set of connected displays and their resolutions). When a monitor is unplugged or the resolution changes, any window left off-screen is moved back onto the nearest visible display.
- **Per-monitor DPI aware (v2):** stays sharp when dragged between screens with different scaling.
- Minimum size is the Mini layout; there is no maximum.

## Look and feel

Chosen direction: **Frosted Dark**, frosted glass meets Squeezebox hardware. iOS-style translucent panels over blurred cover art, with a nod to the old Squeezebox through analogue VU meters and a display-style readout. See the mock-ups canvas.

- **Glass:** sidebar, transport bar and dialogs are frosted panels over a blurred, darkened version of the current album art. Windows 11 Mica/Acrylic backdrop used for the window frame where available.
- **Colour** comes from the cover art: an accent picked from the current album, applied sparingly (play button, progress, active nav item).
- **Meters:** a pair of needle VU meters with a warm backlit face, ballistics close to real VU (~300 ms). Switchable with an OLED-style bar graph (segmented left and right bars with peak hold): click the meters to toggle, or set it in Settings › Appearance.
- **Display readout:** the signal path and time shown in a clean monospaced display face (Share Tech Mono, cyan with a soft glow). It echoes the Squeezebox display without using a pixel font.
- **Theme:** Frosted Dark is the default. A Light view is a full equal, not an afterthought (see Skins).
- **Typography:** Segoe UI Variable for UI text; the display face only for the readout.
- **Motion:** short, calm transitions (150–250 ms); respects Windows "reduce animations".

**Skins:**

- A skin is a named theme package: colour tokens, glass treatment (blur and tint), meter style and readout colour. Each is a small file in the skins folder, so skins can be shared. Build the UI on CSS custom properties so a skin is just a token set.
- Built in: **Frosted Dark** (default), **Frosted Light**, **Squeezebox Classic** and **Hi-fi Amber**.
- **Automatic switching** (Settings › Appearance): follow Windows light/dark (default pair: Frosted Dark and Frosted Light), switch on a schedule (set times, or sunset and sunrise), or stay fixed. Switches fade over about 250 ms.
- Optional override per window mode (for example, Mini always uses Squeezebox Classic).

## Windows integration

**Tray icon:**

- Left-click: show/hide the main window.
- Right-click menu: Play/Pause, Next, Previous, New playlist, Output device ▸, Player ▸, Window mode ▸, Skin ▸, Settings, Quit.
- Mouse wheel over the icon: volume.
- Tooltip: current track and artist.

**Close (X):** hides to tray and keeps playing (default). The first time, a one-off toast explains this. Quit only from the tray menu or File ▸ Quit. Configurable in settings.

**Media integration:**

- Keyboard media keys (play/pause, next, previous) control WinSqueeze.
- Windows media overlay (System Media Transport Controls): track, artist and art in the volume flyout and lock screen, with working controls.
- Taskbar thumbnail buttons: previous, play/pause, next.
- Track-change toast notifications: available, off by default.

**Keyboard shortcuts (rebindable in settings):** Space play/pause; Ctrl+Left/Right previous/next; Ctrl+Up/Down volume; Ctrl+F search; Ctrl+N new playlist; Ctrl+1/2/3 window mode; Ctrl+, settings; Ctrl+Q quit. Optional global hotkeys, off by default.

## Settings and servers

A separate Settings window with tabs, in standard Windows style. Changes apply immediately (no OK/Apply). First run opens it straight on the Servers tab.

| Tab | Settings |
| --- | --- |
| General | Start with Windows; start minimised to tray; minimise to tray when minimised; close (X) hides to tray / quits; remember window position; language |
| Servers | Server finder (auto-discovery); profiles list (name, address, port, optional username/password); default profile; test connection |
| Audio | Output device; driver type; exclusive mode on/off; volume mode (fixed / digital); Squeezelite player name; extra Squeezelite arguments (advanced); fidelity warnings |
| Library & cache | Album source (tags / folders); contributor grouping (any credit / album artist text only); name separators; refresh interval; Refresh now; Rebuild cache; cache size and location; clear art cache |
| Appearance | Skin; auto-switch rule (follow Windows / schedule / fixed); light and dark skin pair; meter style (VU needle / OLED bars); album list default (grid / list); accent from cover art on/off; reduce transparency |
| Shortcuts | Rebind in-app shortcuts; enable global hotkeys; media keys on/off |
| Notifications | Track-change toasts; device missing alerts |
| About | Version; check for updates; licences (Squeezelite GPL notice and source link); logs folder |

**Server finder:** uses Lyrion's UDP discovery on port 3483 to list servers on the local network, showing name, address and version. Manual address entry for VPN or other subnets.

**Profiles:** several servers (e.g. home, office) can be saved and switched from settings or the tray. Each profile has its own cache and remembered player/output. Switching restarts Squeezelite pointed at the new server.

## Install, updates and licensing

- **Installer:** per-user, no admin rights, from GitHub Releases. Installs to `%LOCALAPPDATA%\Programs\WinSqueeze`, adds Start menu entry, optional Start with Windows.
- **Portable zip:** same app, runs from any folder; settings and cache stored next to the exe when a `portable.txt` marker is present.
- **Prerequisites:** Windows 10 21H2+ / Windows 11, x64. WebView2 runtime (bootstrapped by the installer if missing). .NET 8 bundled self-contained.
- **Updates:** checks GitHub Releases on launch (setting to disable). Asks before downloading; never installs silently.
- **Repo:** GitHub repository `winsqueeze`, private during development and public from the first release, with `/app` (C# shell), `/ui` (web front end) and `/vendor/squeezelite` (pinned binary + licence + source link).
- **Licensing:** Squeezelite is GPLv3. Ship its licence and a link to its exact source with every release; WinSqueeze itself released under GPLv3.

## Risks to verify and build phases

**Verify before building on them:**

| Risk | Why it matters | Check |
| --- | --- | --- |
| WASAPI exclusive mode in the Windows Squeezelite build | Bit-perfect promise depends on it | Test the pinned build with the AudioQuest DragonFly at 44.1 and 96 kHz; if unsupported, build Squeezelite with PortAudio WASAPI exclusive enabled |
| Meter level source | Exclusive mode bypasses Windows loopback capture, so levels can't be read from the OS | Use Squeezelite's visualiser/level export if available on Windows, otherwise a small patch exposing peak levels via shared memory or stdout |
| Delta sync | Lyrion has no clean "changed since" query | Use last-scan time + totals to detect change, then compare track ids/modification data; full rebuild as fallback. Measure full-fetch time on a 200k-track test library |
| Folder albums at scale | Grouping 200k track URLs | Done once at sync time into an indexed table, not at browse time |
| Server-side transcoding | Server prefs could force MP3/FLAC re-encoding | Read player prefs and the server's convert rules; warn only |
| Contributor name splitting | Separators vary across libraries | Configurable separator list in Library settings |

**Build phases (stop for review after each):**

1. **Shell + player:** C# tray app, bundled hidden Squeezelite, output picker, server finder, start/stop, settings window skeleton. Verify exclusive mode first.
2. **Cache (synchronising client):** SQLite index, full sync, change detection, art cache.
3. **Browse UI:** WebView2 front end with sidebar, Artists, Albums, Genres, Composers, Performers, Folders, Favourites, search (including folder-grouped results), album view.
4. **Transport, queue and playlist builder:** bottom bar, expanded now-playing, queue editing, playlist builder, player switcher, media keys, SMTC, taskbar buttons.
5. **Look:** Frosted Dark and Frosted Light skins, skin switching, VU and OLED meters, display readout, window modes including Mini, multi-monitor handling. Then the Squeezebox Classic and Hi-fi Amber skins.
6. **Ship:** installer, portable zip, update check, licences, README.
