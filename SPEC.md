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
- **Squeezelite:** pinned FFmpeg build, launched hidden with `-s <server> -n <player name> -o "<device name> [<host API>]" -a 200:1` (exclusive; 40 ms latency caused stream restarts on the DragonFly), restarted on device or server change, stopped on quit. Registers with Lyrion as a normal player.
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

**Album lists (second-level browsing):** a toolbar at the top right of every album view with three glass dropdowns, each remembered per area:

- **Sort:** album artist (then year), title, year, recently added.
- **Group:** none, album artist, year, decade, date added (month), format, first letter. Defaults: Genres and Composers and Performers by album artist; Artists and Albums ungrouped (an artist keeps "Albums" and "Also appears on", grouped inside each).
- **View:** Large covers (default), Compact (small covers), List.

Settings › Browsing chooses which groupings the Group menu offers and each area's starting grouping.

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
- **Meters** are driven by the real playback level of the bundled Squeezelite (see Risks). For other players, they show a muted idle state. Three styles, cycled by clicking the meters: analogue **VU needles**, **OLED VU** (needle meters drawn as a glowing display) or a **spectrum analyser** (segmented cyan columns in a recessed glass window, matching the EQ). **Settings › Meters** tunes them live: 0 VU reference (default −18 dBFS), VU ballistics, spectrum floor/top, tilt and fall.
- **Signal readout:** format, sample rate, bit depth, and a "bit-perfect" badge when the whole chain is untouched.

## Playback and fidelity

Fidelity is a headline feature, not an afterthought. Playback always goes through the bundled Squeezelite (FFmpeg build), never the browser engine.

1. **Lossless end to end.** The server sends original files in their native format (FLAC, ALAC, WAV, AIFF, hi-res). Squeezelite decodes locally. No MP3 transcoding.
2. **Bypass the Windows mixer.** Default output is the device's WASAPI endpoint in exclusive mode, giving bit-perfect output with no other system sounds mixed in. Falls back to shared mode with a visible warning if exclusive isn't available.
3. **Native sample rates.** 44.1, 48, 88.2, 96, 176.4 and 192 kHz pass straight through; the DAC switches rate per track. Only rates the device can't take (e.g. 22.05 kHz internet radio in exclusive mode) are resampled (Squeezelite `-R hE`), so they play instead of failing.
4. **Volume:** by default Squeezelite scales the audio, independent of Windows' volume. Opt-in (Settings › Audio): where the device has hardware volume (e.g. the DragonFly), WinSqueeze's volume control drives the Windows endpoint volume, which stays bit-perfect even in exclusive mode. It is the same as Windows' volume for that device, so it works both ways: turning Windows' volume moves the server volume and every slider. Off by default. Optional Squeezelite digital volume, clearly labelled as not bit-perfect. (Implemented with WinSqueeze's own Squeezelite flag `-F`, fixed full-level output, plus Core Audio endpoint volume following the server's volume for this player.)
5. **Server-side check (read-only).** Read the player's bitrate-limit and transcoding prefs from the server. If anything would force transcoding, show a warning explaining which server setting to change. Never change it.
6. **Gapless** playback preserved (native Squeezelite behaviour).
7. **Signal path on screen:** source format → decoder → output mode → device rate, with the bit-perfect badge.

Bandwidth note: CD-quality FLAC needs about 1–1.5 Mbps, hi-res 24/96 about 3–5 Mbps; fine for typical VPN links.

## Equaliser

- Opened by clicking the transport's spectrum analyser. Its own window, styled like a classic hi-fi graphic EQ: black brushed panel, gold legends, a fader bank per channel (±12 dB, true dB), a recessed cyan analyser between them (10 octave bands, L+R or L/R display, fast or slow decay), and a smoked-glass reflection over the front.
- **Bands:** 10 (octaves 31.5 Hz–16 kHz), 7 (63 Hz–16 kHz, 1⅓-octave spacing) or 3 (bass shelf 150 Hz, mid 1 kHz, treble shelf 5 kHz).
- **Character:** Vintage (default) models an analogue gyrator EQ: bands are broad at gentle settings and narrow to about an octave at full boost. Precise keeps a constant Q.
- **Stereo:** linked (one curve, both banks move together) or L/R independently, with copy L→R and R→L. Reset sets the current band layout flat.
- **Editing:** Draw (default) sweeps across the faders and each one jumps to the pointer; Single moves only the fader you grabbed. Shift swaps the mode while held. Hovering highlights the fader and shows its frequency and gain; double-click returns it to 0 dB; the wheel steps 0.5 dB.
- **Processing:** inside our Squeezelite, before volume (biquads from WinSqueeze via shared memory, redesigned for each sample rate). Preamp: MANUAL (default, fixed −6 dB headroom, adjustable 0 to −12 dB) so raising a band only ever makes that band louder, or AUTO (lowered as bands rise so the curve's peak stays at 0 dBFS). A transparent safety limiter (instant attack, ~150 ms release, −0.2 dBFS ceiling) catches any overshoot, and a red CLIP lamp lights while it works. The big square button bypasses it completely: bypass is bit-perfect and the readout's badge reflects it.
- **Docking:** the window snaps magnetically to any edge of the main window and then moves with it; dragging it away undocks it. It keeps its own size whatever the main window's mode. When docked, resizing it snaps an edge to the main window's matching edge within 12 px, so it takes the same width or height.

## Recording

Record a radio station (or any other stream) to FLAC, for personal listening.

- **Button:** a small red ring beside the title in the transport bar, shown only while this PC's player plays a stream (Lyrion's `remote`). Never for library files, never for other players. Click to record: it turns solid red, pulses and shows the elapsed time; click again to stop. Settings › Recording can hide it completely.
- **Capture:** inside our Squeezelite (`vendor/squeezelite/src/output_rec_win.c`, shared memory `Local\squeezelite-rec-<mac>`). The decoded audio about to play, before EQ and volume, goes through a ring to a writer thread that encodes 24-bit FLAC at the stream's own sample rate, so the audio thread never touches the disk. A rate change mid-recording continues in "(part 2)".
- **Files:** by default one folder per recording, `<folder>\<station> - yyyy-MM-dd, HH.mm.ss\NN <artist> - <title>.flac` (numbered in order; ALBUM = station and start time, plus TRACKNUMBER, so each recording is one album). Settings › Recording can instead use one folder per station, `<folder>\<station>\<yyyy-MM-dd HH.mm> <artist> - <title>.flac` (ALBUM = station). Also tagged TITLE, ARTIST, DATE and COMMENT. Default folder `Music\WinSqueeze recordings`. With "new file per song" (default on), each song change the station announces starts a new file (timing follows Lyrion's delayed title change, so cuts are approximate).
- **Ends** when you click stop, the player leaves the stream for a library file or stops, or WinSqueeze quits (the file is finished cleanly). A Squeezelite restart (device change) finishes the file and carries on in a new one.
- A toast says where the files went. The UI and Settings say recordings are for personal use only.

## Output device selection

1. **Picker in the app:** a dropdown in the transport bar's device indicator, in settings (Audio tab) and in the tray menu.
2. **Friendly names, grouped by driver:** WASAPI, ASIO (if present), DirectSound/MME. Each shows whether it supports exclusive mode.
3. **One-click switch:** restarts the hidden Squeezelite on the new device; if it was playing, playback resumes at the same position.
4. **Remembered by name and driver, never by index.** Windows device numbers change when hardware is plugged in.
5. **Player name:** the name this PC's player shows in the server's player list and in every other controller. Defaults to the Windows computer name; the user can change it in Settings › Audio, and the change applies straight away (Squeezelite restarts with the new `-n`). Each install also keeps its own stable player MAC, so it never collides with another Squeezelite on the same PC.
6. **Missing device at startup:** show a clear warning and either wait for it to return or use a user-chosen fallback. Never silently play through laptop speakers.
7. **Hot-plug:** device list refreshes automatically when a USB DAC is connected or removed; if the chosen device returns, switch back to it.

## Window modes and layout

One resizable window that changes layout by its own size. Breakpoints are in device-independent pixels, so they scale with Windows display scaling.

| Mode | Triggered when | Layout |
| --- | --- | --- |
| Desktop | Wider than ~900 | Left sidebar, main browse area, bottom transport bar |
| Vertical | ~420–900 wide, taller than wide | Sidebar becomes a bottom tab bar above the transport bar; single-column browsing |
| Mini | Smaller than ~420 × ~260 | Winamp-style strip, roughly 2:1: small art, title, artist, prev/play/next, small switchable meters, seek line; the speaker icon (or the mouse wheel) swaps the seek line for a volume slider. No browsing |

- Modes switch automatically as the window is resized, and Ctrl+1/2/3 jumps straight to a mode (restoring the size last used for it).
- Selected text can be copied (Ctrl+C or right-click › Copy); clicking elsewhere clears the selection.
- Mini mode has an **always-on-top** pin. It floats freely; no edge snapping.
- After an update, Mini shows the same kind of dot for What's new; its notice has a Details button that opens the full window and the card.
- When an update is ready, Mini shows a small pulsing accent dot in the top-right corner; clicking it slides the "Restart to update" notice down over the player.
- **Multi-monitor:** size, position and mode are remembered per monitor configuration (keyed by the set of connected displays and their resolutions). When a monitor is unplugged or the resolution changes, any window left off-screen is moved back onto the nearest visible display.
- **Per-monitor DPI aware (v2):** stays sharp when dragged between screens with different scaling.
- Minimum size is the Mini layout; there is no maximum.

## Look and feel

Chosen direction: **Frosted Dark**, frosted glass meets Squeezebox hardware. iOS-style translucent panels over blurred cover art, with a nod to the old Squeezebox through analogue VU meters and a display-style readout. See the mock-ups canvas.

- **Glass:** sidebar, transport bar and dialogs are frosted panels over a blurred, darkened version of the current album art. Windows 11 Mica/Acrylic backdrop used for the window frame where available.
- **Colour:** an accent chosen in Settings › Appearance (colour picker, reset to the default blue), or optionally (off by default) one picked from the current album's cover in the frosted skins, applied sparingly (play button, progress, active nav item). Text on the accent switches between dark and white for contrast.
- **Now playing backdrop:** the blurred colours of the current cover behind Now playing, on by default; Settings › Appearance can turn it off for the plain skin background.
- **Glass:** Appearance sliders for the glass tint's transparency and hue (neutral by default), previewed live, with a reset. The glass stays frosted when another app has focus (Windows would turn it solid); an Appearance checkbox restores Windows' behaviour.
- **Meters:** a pair of needle VU meters with a warm backlit face, ballistics close to real VU (~300 ms). Reference: `docs/reference/analogue-vu-reference.png` (cream face, lamp glow falling off upward and outward from below). Also an **OLED VU**: the same two needle meters drawn as a Squeezebox Transporter-style display, with glowing cyan arc scales from 20 to +3 that widen into the red zone, VU labels and thin glowing needles on black glass (reference: `docs/reference/oled-vu-reference.png`). Third style: an OLED-style bar graph (segmented left and right bars with peak hold). Click the meters to cycle, or set it in Settings › Appearance.
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

**Other Squeezelite instances:** at launch, WinSqueeze looks for any other Squeezelite running on the PC, even one whose exe has been renamed. Candidates are processes connected to a server's player port (TCP 3483) or with "squeezelite" in their name, confirmed by Squeezelite's own banner text inside the exe. For each one it offers to stop it, and if a Startup-folder shortcut or HKCU Run entry launches it at login, to switch that off (moved aside, never deleted). Settings › General sets this to Ask (default), Always stop or Leave it running.

**Close (X):** hides to tray and keeps playing (default). The first time, a one-off toast explains this. Quit only from the tray menu or File ▸ Quit. Configurable in settings.

**Media integration:**

- Keyboard media keys (play/pause, next, previous) control WinSqueeze.
- Windows media overlay (System Media Transport Controls): track, artist and art in the volume flyout and lock screen, with working controls.
- Taskbar thumbnail buttons: previous, play/pause, next.
- Track-change toast notifications: available, off by default.

**Keyboard shortcuts (rebindable in settings):** Space play/pause; Ctrl+Left/Right previous/next; Ctrl+Up/Down volume; Ctrl+F search; Ctrl+N new playlist; Ctrl+1/2/3 window mode; Ctrl+, settings; Ctrl+Q quit. Optional global hotkeys, off by default.

## Diagnostics

Unhandled errors, background task errors, WebView2 renderer failures and UI-thread freezes over 8 seconds (hang watchdog) are written to `logs\crash.log` with the version and the last shell activity. The next launch shows a tip pointing to it. Squeezelite's log keeps at most 50 lines a second, so a retry loop can't flood it.

## Settings and servers

A separate Settings window with tabs, in standard Windows style. Changes apply immediately (no OK/Apply). First run opens it straight on the Servers tab.

| Tab | Settings |
| --- | --- |
| General | Settings export / import (JSON; everything except passwords and other secrets); Start with Windows; start minimised to tray; other Squeezelite at launch (ask / always stop / leave); minimise to tray when minimised; close (X) hides to tray / quits; remember window position; language |
| Servers | Server finder (auto-discovery); profiles list (name, address, port, optional username/password); default profile; test connection |
| Audio | Output device; driver type; exclusive mode on/off; volume mode (fixed / digital); player name (defaults to the computer name); extra Squeezelite arguments (advanced); fidelity warnings |
| Library & cache | Album source (tags / folders); contributor grouping (any credit / album artist text only); name separators; refresh interval; Refresh now; Rebuild cache; cache size and location; clear art cache |
| Appearance | Skin; auto-switch rule (follow Windows / schedule / fixed); light and dark skin pair; meter style (VU needle / OLED VU / OLED bars); album list default (grid / list); accent from cover art on/off; reduce transparency |
| Shortcuts | Rebind in-app shortcuts; enable global hotkeys; media keys on/off |
| Notifications | Track-change toasts; device missing alerts |
| Recording | Show the record button; new file per song; save folder (choose / open) |
| About | Version; update channel (stable / dev); version switcher with per-version comments; check now; auto-update on/off; licences (Squeezelite GPL notice and source link); logs folder |

**Server finder:** uses Lyrion's UDP discovery on port 3483 to list servers on the local network, showing name, address and version. Manual address entry for VPN or other subnets.

**Profiles:** several servers (e.g. home, office) can be saved and switched from settings or the tray. Each profile has its own cache and remembered player/output. Switching restarts Squeezelite pointed at the new server.

## Install, updates and licensing

- **Two downloads, each a single file:**
  - **Portable:** `WinSqueeze-Portable.exe`, one self-contained 64-bit exe with the web UI and Squeezelite embedded (unpacked to the data folder on first run). Settings and cache live next to the exe in a `WinSqueeze-data` folder. It updates itself in place (download, swap on restart).
  - **Installer:** `WinSqueeze-Setup.exe`, per-user, no admin rights. Installs to `%LOCALAPPDATA%\WinSqueeze`, adds a Start menu entry, optional Start with Windows, and updates through Velopack. Its only advantage is not having to remember where the portable exe lives.
- **Code signing:** releases are signed so Windows trusts the publisher. Preferred: SignPath Foundation (free for open-source projects, signs from GitHub Actions). Fallback: Azure Trusted Signing. SmartScreen reputation still builds over the first weeks of downloads.
- **Icon:** a custom app icon in the Frosted Dark style (tray, taskbar, installer, exe).
- **Prerequisites:** Windows 10 21H2+ / Windows 11, x64. WebView2 runtime (bootstrapped by the installer if missing). .NET 8 bundled self-contained.
- **Updates:** see Updates and versions below.
- **Repo:** GitHub repository `winsqueeze`, private during development and public from the first release, with `/app` (C# shell), `/ui` (web front end) and `/vendor/squeezelite` (pinned binary + licence + source link).
- **Licensing:** Squeezelite is GPLv3. Ship its licence and a link to its exact source with every release; WinSqueeze itself released under GPLv3.

## Updates and versions

Updating should be effortless, and is how the author tests every build.

- **Updater:** Velopack, which installs per user, downloads in the background, stages the new version and applies it on restart. The same packages serve the installer and the portable zip.
- **Checking:** on launch, then every 15 minutes on stable and every 20 seconds on dev. Checks never use GitHub's API (60 anonymous calls an hour): dev reads the releases Atom feed with `If-None-Match`, stable follows the `/releases/latest` redirect, and Velopack then reads that release's own feed from its download URL. Only the version list in Settings › About calls the API, on demand.
- **Seamless apply:** a found update is downloaded and staged silently. A small, calm notice ("WinSqueeze 0.4 is ready. Restart to update") appears in the window and as a tray balloon. One click relaunches into the new version, resuming playback. Nothing is installed without that click, and auto-download can be turned off.
- **Channels:** every push to `main` is published as a *dev* pre-release, and deliberate releases (run the build by hand with a version, or push a tag `vX.Y.Z`; notes from `docs/releases/<version>.md`) go out on *stable*. Dev builds are numbered just above the latest stable (after 1.0.0 they are 1.1.N), so the dev channel is always ahead. Users choose a channel in Settings › About; the default is stable.
- **Version switcher:** Settings › About lists available versions (releases and, on the dev channel, dev builds) with date and notes. Picking one downloads it and relaunches into it, downgrades included.
- **Comments per version:** each release has its own GitHub Discussion (not an Issue). From the version switcher a user can read and add comments for that version. Posting signs into GitHub once using the device-code flow (a short code entered on github.com), then posts to that release's discussion under the user's own name. The comment box states clearly, next to the Post button, that comments are public on GitHub and show the user's GitHub name. Anonymous comments would need a small relay server and are out of scope for now.
- **Repo:** in-app updates need the release assets to be public.



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
5. **Look:** Frosted Dark and Frosted Light skins, skin switching, VU, OLED VU and OLED bar meters, display readout, window modes including Mini, multi-monitor handling. Then the Squeezebox Classic and Hi-fi Amber skins.
6. **Ship:** installer, single-file portable exe, icon, code signing, update check, settings export/import, licences, README.
