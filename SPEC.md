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

1. **Server untouched.** Read-only use of the library API. Never write server settings or plugin state. The only commands sent are normal player control (play, pause, queue, volume, shuffle, power, sleep), the player's own crossfade and ReplayGain prefs when the user changes them in Settings › Playback, favourites or saved playlists the user explicitly asks for, and library scans the user starts in Settings › Server › Library.
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
- **Squeezelite:** pinned FFmpeg build, launched hidden with `-s <server> -n <player name> -o "<device name> [<host API>]" -a 200:0` (shared; `-a 200:1` when exclusive mode is on; 40 ms latency caused stream restarts on the DragonFly), restarted on device or server change, stopped on quit. Registers with Lyrion as a normal player.
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

Name matching splits on common separators (`,` `;` `&` `/` `feat.` `with` `and`) and is case- and accent-insensitive. The separator list is configurable. Artist and album artist names never split on commas or get turned round: a name with a comma stays whole ("Earth, Wind & Fire"). For composers and conductors, a name written "Surname, Forename" (one word before the comma, up to three after: "Kapustin, Nikolai", "Tchaikovsky, P.I.", "Bach, Johann Sebastian") is one person, not two, and is matched as "Nikolai Kapustin" (a Settings › Library tickbox, on by default). Settings › Library chooses how composers and conductors are shown and sorted: "Surname, Forename" (default; the last word is taken as the surname) or "Forename Surname".

**Refresh:**

- On launch and every few minutes, poll `serverstatus` for last-scan time and library totals. If unchanged, do nothing.
- If changed, fetch a delta (tracks added or modified since the last sync) and remove tracks that no longer exist. Fall back to a full background rebuild if the delta can't be trusted. Browsing keeps working from the old cache meanwhile.
- Manual "Refresh library" and "Rebuild cache" in settings.
- Never sync while the server is scanning (`serverstatus` `rescan` 1): its library is partial until the scan ends.
- The first build shows a progress bar; the app is usable as soon as the first pages land.

## Browsing

Sidebar sections, in this order: Favourites, Radio, Apps, Artists, Genres, Composers, Performers, Folders, Queue.

| Section | List shows | Click opens |
| --- | --- | --- |
| Favourites | Lyrion favourites: streams (Radio 3, Radio 4), random mixes (e.g. jazz), albums, artists, playlists | Plays it, or opens it if it's a container |
| Radio | The server's radio plugins (`radios`), as an icon grid | That plugin's menu |
| Apps | The server's apps and streaming services (`apps`: Qobuz, TIDAL, Spotty, BBC Sounds, podcasts…), as an icon grid | That app's menu |
| Artists | One merged list of every artist and album artist (no separate "album artist" concept) | That artist's albums: their own first, then a separate "Also appears on" group of contributing albums |
| Genres | All genres | Albums in that genre, grouped under album artist headings, with contributing albums folded under the main artist as above |
| Composers | Everyone credited as composer | Albums (CDs) containing at least one work by them, not a list of works |
| Performers | Merged conductors, bands/orchestras/ensembles and soloists, with a role filter chip | Albums they appear on |
| Folders | The music folder tree as stored on disk | Subfolders; a folder of tracks opens as an album |
| New music | Off by default (Customise sidebar adds it). The newest 100 albums by when the server first saw them (Lyrion's added time, kept through a clear and rescan; an album counts from its newest track), grouped by month added, newest first; no Sort menu | The album |
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

**Page header:** every browse page (top level, second level and custom views) has a pinned title bar: a big title with its count on the left, and the page's controls and view switcher on the right. It stays in place while the page scrolls.

**List, Cards or Tiles:** Artists, Genres, Composers, Performers and every custom view step switch between List, Cards and Tiles in the header (remembered per list). A card is a taller glass row with one cover (or the first letter) on the left, the name and its count in bold. A tile is a square mosaic of up to four covers from inside it (newest first: one fills the square, two split it, three are one large and two small), with the name and its count in bold.

## Custom views

The built-in lists browse the whole library one way: pick a composer, see their albums. A custom view first narrows the library to genres you choose, then steps through the order you choose (for example Genre, then Composer, then albums). A Classical view therefore lists only classical composers, without the pop songwriters credited on other albums, and answers questions the built-in lists can't, such as which composers you have in Choral or which genres you have of Bach. (The successor to Custom Browse and Multi Library menus from the old Logitech Media Server, built into WinSqueeze so it needs no server plugin or setup.)

- **A view** has a name, an on/off switch, an optional sidebar menu (shown as "Sidebar menu"; stored as Group), a genre list (empty = whole library) and up to four steps, each one of Genre, Composer, Artist (album, track or guest artist) or Performer (conductors, bands and orchestras). After the last step it shows albums, with the usual Sort, Group and View toolbar.
- **Scope:** a track belongs to a view when it has one of the view's genres and matches every step chosen so far. Genre steps list only the view's own genres. Counts are albums and tracks within the scope. Selecting rows and playing them plays only the tracks in scope.
- **Sidebar:** switched-on views appear in the sidebar. Views with the same sidebar menu sit in one entry of that name, which opens a page of views shown as List, Cards or Tiles (the header switcher, remembered). Press and hold a card or tile and drag it to reorder; the order is saved (the Custom views page has the same order, with drag and arrows). Each card has a mosaic of covers from inside it, its full name and steps, its album count, and its first step's biggest rows as chips with album counts that jump straight in; a view with no group gets its own entry. They can be moved and hidden like any section.
- **Editing:** a Custom views page in the main window (from Customise sidebar, from a group's page, or Settings › Browsing, which also carries the explainer above). Changes save at once, in the settings file, so settings export and import carry them.
- **Demos**, switched off out of the box, all in group "Classical" and limited to Baroque, Chamber Music, Choral, Classical, Minimalist, Opera, Orchestral, Piano Music, String Music and Symphony: Composers (composer, then albums), Composers by genre, Genres by composer, Composers by artist.

## Album view, queue and favourites

**Album view (CD view):** opening an album shows large cover art, album artist, year, format summary (e.g. FLAC 24/96), then the track list grouped by disc. In folder mode, a CD holding works by several composers shows each track's composer in a quiet line under the title.

- Buttons: **Play** (replaces the queue), **Play next**, **Add to queue**, **Favourite** (star).
- Track row: click plays from that track onward; right-click or ⋯ menu has Play next, Add to queue, Go to artist, Go to composer, Open folder.

**Queue:** full view in the sidebar section and inside the expanded now-playing view. Drag to reorder, remove, clear, save as Lyrion playlist. Shows total remaining time. Clear can be undone (same tracks, track and position).

**When the queue ends** (Up next header, remembered): Stop, Repeat all (Lyrion's repeat-all on the player), or Keep playing. Keep playing adds a whole album or about ten tracks just before the last track ends (under 20 s left), looking at the same album artist, then the playing track's genres, then anything; it skips anything queued or added by Keep playing in the last 3 days, and each top-up can be undone. Radio and remote streams are left alone.

**Play order** (same Up next menu, above "When the queue ends"): In order, Shuffle tracks or Shuffle albums (Lyrion's `playlist shuffle`). No extra control: the menu's button shows a small ⤮ only while shuffle is on. Hidden offline.

**Long tracks** (20 min and up) remember where you left them partway (more than a minute in and a minute from the end); playing one again carries on from there, with Undo to start from the beginning. Kept per device for the last 50 such tracks.

**Favourites:** star any album, artist, folder, playlist or stream from anywhere; unstar the same way. Changes go to Lyrion's own favourites, so they stay in sync with other controllers. Only on user action.

**Radio and apps:** no plugin gets its own code. Every radio and app plugin serves Lyrion's shared menu format (`<cmd> items`, the same one Favourites uses), so one browser covers them all: folders open, streams and episodes play (Play now / next / add to end via `<cmd> playlist`), search entries ask for words, and anything with an address can be added to favourites. Any app page can be pinned to the sidebar, so a much-used app gets its own entry without crowding the sidebar for people with many plugins. Radio and streams show their own artwork (`artwork_url`) wherever playback was started.

**Players:** a player switcher in the transport bar. Defaults to this PC's bundled Squeezelite; any other player on the server (other Squeezeboxes in the house) can be picked and controlled the same way.

- **Power:** each connected player in the switcher has an on/off button (Lyrion's `power`); a player in standby reads "off".
- **Multi-room (sync groups):** each other player in the switcher has a link button that joins it to the selected player's group (Lyrion's `sync`) or takes it out. Grouped players sit indented under the selected one, each with its own thin volume slider; the main volume stays the selected player's. The switcher button reads "Living room + 2" while grouped; a player in another group shows "with …". Nothing else in the main window changes. Hidden offline.
- **Sleep timer:** in the switcher menu (15, 30, 45, 60, 90 min, End of track, Off) and the tray menu, for the selected player (Lyrion's `sleep`). While it runs, a small moon shows on the switcher button. Power and sleep are hidden offline.

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
- Clicking the art or title opens the **expanded now-playing view**: large art over a blurred-art backdrop, full metadata (composer, performers, album in italics with its year in brackets from the tags, programme-notes style), signal path readout, large meters, and the queue alongside.
- **Meters** are driven by the real playback level of the bundled Squeezelite (see Risks). For other players, they show a muted idle state. Three styles, cycled by clicking the meters: analogue **VU needles**, **OLED VU** (needle meters drawn as a glowing display) or a **spectrum analyser** (segmented cyan columns in a recessed glass window, matching the EQ; segments brighten up the scale, and a floating peak segment holds about 0.8 s over each column before drifting down). All spectrum displays (the small one by the volume, Now playing's and the EQ window's) use the same 10-octave analysis and scale. By default the meters show the sound after the EQ but before WinSqueeze's volume, and 0 VU follows each track's average loudness. **Settings › Meters** can switch each of those (follow EQ, follow volume, automatic 0 VU), and tunes the fixed 0 VU reference (−18 dBFS), VU ballistics and spectrum fall. Our Squeezelite exports two rings (before and after the EQ, both before the volume) plus the volume gain.
- **Signal readout:** format, sample rate, bit depth, and a "bit-perfect" badge when the whole chain is untouched.

### Now playing views

The queue panel in Now playing has tabs along its top: **Up next** (the queue) on its own, then a group about the playing track, led by a small copy of its cover: **Artist** (biography and photo), **Album** (review and background) and **Lyrics**. The group follows the track; Up next stays as it was. Lyrics are looked up as each track starts, so the Lyrics tab greys out when there are none. Unticking all three tabs in Settings gives the plain Up next panel. The chosen tab is remembered. The mini player has no tabs.

Each tab tries its ticked sources in order and shows the first that has something, with the source named underneath (and a link where there is one):

| Tab | Sources (default order) |
|---|---|
| Artist | Server plugin (Lyrion's Music & Artist Information: `musicartistinfo biography` and `artistphoto`), Wikipedia (the artist found on MusicBrainz, its Wikidata link naming the English Wikipedia page, whose introduction is shown), Last.fm (only with the user's own API key; off until one is entered) |
| Album | Server plugin (`musicartistinfo albumreview`), Wikipedia (the release group found on MusicBrainz, then as for Artist), Last.fm |
| Lyrics | The file's own tags (Lyrion's lyrics tag), server plugin (`musicartistinfo lyrics`), LRCLIB |

The Artist tab is the bio (its opening, then Show more; the opening never ends on a heading), with a **View artist timeline** button above it, badged with a small drawn timeline, when there is one; the Timeline has "← Artist bio" to go back, and a new artist opens on the bio. The Album tab works the same way: the album text, with a **View album credits** button above it (badged with a small drawing of the credits list) when there are credits; Credits has "← Album" to go back, and a new album opens on its text. Above the album text, **From your music files** lists what the album's own tags say (from the library index, local tracks only): album artist, other track artists, composers, conductor, orchestra, genre, year (a range if tracks differ; Lyrion keeps only the year), tracks, length and discs, and format per kind of file (lossless as bit depth and sample rate, lossy as bitrate with its CBR/VBR mode, a range when VBR tracks differ, with track counts when the album mixes formats). The online text follows under **From** its source, with Read more. As each track starts, every ticked tab's lookups (and Timeline and Credits) are asked for straight away, so they're ready before a tab is opened; the shell keeps the answers for the session.

**Lyrics view.** Lyrics follow the song wherever the source has timed (synced, LRC) lyrics: LRCLIB's synced lyrics (a timed hit of the right length is preferred to an untimed exact match), or timestamps in the file's tags or from the plugin. The sung line is lit (brighter and a little heavier), lines already sung stay a shade brighter than those to come, and the list keeps the sung line about a third of the way down; scrolling by hand pauses that for a few seconds. Clicking a line plays from it. Untimed lyrics show as verses with nothing lit. Lyrics are set in Newsreader (bundled, OFL), a book face with optical sizes, rather than the UI font. The Lyrics tab has a **full screen** button (kept in view as the lyrics scroll) that opens the lyrics alone on the main window's screen, over the playing cover blurred: the track's small cover, title (italic) and artist at the top, the lyrics in big type below. Esc (or the button) closes it. In the mini player a lyrics button (shown only when the playing track has lyrics) opens the same view in a window of its own, which remembers its size and place and can itself go full screen (F11, the button or a double-click; Esc returns to the window). Space plays and pauses; the controls and, in full screen, the pointer fade while reading. It is never called karaoke.

**Credits** (Settings › Now playing views can turn it off) lists everyone on the album from MusicBrainz relationships: the release's own (producer, engineers, mastering, artwork, photography, liner notes), each recording's (instruments, vocals, mixing, recording) and each song's writers (composer, lyricist, arranger). MusicBrainz often credits only one of an album's pressings, so the three earliest official releases are read and the best-credited one is used; the source line names it. People are grouped as Performers, Writers, Production and Artwork and notes, one row each: name, roles, and a strip with one cell per track, filled where they're on it. The playing track's cell (matched by title) is blue only for the people on it, whose rows are highlighted; everyone else's cell stays grey, so the strip has three states: grey (not on that track), light (on it), blue (on the one playing); hovering a row lists each role's tracks. Hovering a cell names that track. A small arrow above each group's strips points down at the playing track's column, with a "Track N" pill on the first group's arrow (tucked in at the right edge for the last tracks). Two first-run hints explain it without adding text to the list: "Each bar is a track in the album" on the first strip, then, once closed, "Highlighted: playing on this track" on the first highlighted row; each sits on whichever row fits now, so it follows the playing track, and stays until closed (Settings › General can hide or restore hints). Opening the Timeline scrolls it so the album playing now sits in the middle of the view. The View artist timeline and View album credits buttons carry a quiet second line: "Bands, labels and albums, year by year" and "Who plays on each track".

Artist and album text is never a disambiguation ("may refer to") page and must name the track's artist, or that source is skipped (this stops loose title matches, e.g. another song of the same name). It is tidied into paragraphs, headings and lists (Markdown, wiki and HTML formatting removed; references and link sections dropped), and long text shows its opening with Show more. The shell does the lookups (MusicBrainz is held to one request a second, every request carries a WinSqueeze User-Agent) and keeps the results for the session. Online sources are sent only the artist, album and track names. The plugin is used only when the server has it (`can musicartistinfo biography ?`).

## Playback and fidelity

Fidelity is a headline feature, not an afterthought. Playback always goes through the bundled Squeezelite (FFmpeg build), never the browser engine.

1. **Lossless end to end.** The server sends original files in their native format (FLAC, ALAC, WAV, AIFF, hi-res). Squeezelite decodes locally. No MP3 transcoding.
2. **Bypass the Windows mixer.** Exclusive mode (Settings › Audio, off by default because some sound card drivers refuse it) uses the device's WASAPI endpoint exclusively, giving bit-perfect output with no other system sounds mixed in. If the device won't open in exclusive mode (e.g. "Invalid device"), WinSqueeze restarts Squeezelite in shared mode for that device until the app restarts, shows a tray note saying so, and the player status reads "shared (device refused exclusive)". Exclusive mode lets go of the device 10 seconds after a pause or stop (Squeezelite `-C 10`), and unticking it restarts Squeezelite in shared mode straight away, so other apps never need WinSqueeze closed. Settings shows an amber note saying so while it's on.
3. **Native sample rates.** 44.1, 48, 88.2, 96, 176.4 and 192 kHz pass straight through; the DAC switches rate per track. Only rates the device can't take (e.g. 22.05 kHz internet radio in exclusive mode) are resampled (Squeezelite `-R hE`), so they play instead of failing.
4. **Volume:** Squeezelite scales the audio, independent of Windows' volume (no link to the device's hardware volume). A badge above the volume slider shows a verified rosette and "Bit-perfect" when the source is lossless at its native rate, output is exclusive, volume is 100 and the EQ is off; below 100 it shows "Scaled", and clicking it explains that setting 100 and using Windows' volume (or the DAC's knob) gives bit-perfect playback.
5. **Server-side check (read-only).** Read the player's bitrate-limit and transcoding prefs from the server. If anything would force transcoding, show a warning explaining which server setting to change. Never change it.
6. **Gapless** playback preserved (native Squeezelite behaviour).
7. **Signal path on screen:** source format → decoder → output mode → device rate, with the bit-perfect badge.

Bandwidth note: CD-quality FLAC needs about 1–1.5 Mbps, hi-res 24/96 about 3–5 Mbps; fine for typical VPN links.

## Equaliser

- Opened by clicking the transport's spectrum analyser (it appears only once its panel is drawn, and closing it just hides it, so it reopens instantly). Its own window, styled like a classic hi-fi graphic EQ: black brushed panel, gold legends, a fader bank per channel (±12 dB, true dB), a recessed cyan analyser between them (10 octave bands, L+R or L/R display, fast or slow decay), and a smoked-glass reflection over the front.
- **Bands:** 10 (octaves 31.5 Hz–16 kHz), 7 (63 Hz–16 kHz, 1⅓-octave spacing) or 3 (bass shelf 150 Hz, mid 1 kHz, treble shelf 5 kHz).
- **Character:** Vintage (default) models an analogue gyrator EQ: bands are broad at gentle settings and narrow to about an octave at full boost. Precise keeps a constant Q.
- **Stereo:** linked (one curve, both banks move together) or L/R independently, with copy L→R and R→L. Reset sets the current band layout flat.
- **Editing:** Draw (default) sweeps across the faders and each one jumps to the pointer; Single moves only the fader you grabbed. Shift swaps the mode while held. Hovering highlights the fader and shows its frequency and gain; double-click returns it to 0 dB; the wheel steps 0.5 dB.
- **Processing:** inside our Squeezelite, before volume (biquads from WinSqueeze via shared memory, redesigned for each sample rate). Preamp: MANUAL (default, fixed −6 dB headroom, adjustable 0 to −12 dB) so raising a band only ever makes that band louder, or AUTO (lowered as bands rise so the curve's peak stays at 0 dBFS). A transparent safety limiter (instant attack, ~150 ms release, −0.2 dBFS ceiling) catches any overshoot, and a red CLIP lamp lights while it works. The big square button bypasses it completely: bypass is bit-perfect and the readout's badge reflects it.
- **Docking:** the window snaps magnetically to any edge of the main window and then moves with it; dragging it away undocks it. It works both ways: dragging the main window near the undocked EQ snaps the main window to its edge (for example underneath it), and the two then move together. It keeps its own size whatever the main window's mode. When docked, resizing it snaps an edge to the main window's matching edge within 8 px, so it takes the same width or height.

## Recording

Record a radio station (or any other stream) to FLAC, for personal listening.

- **Button:** a small red ring beside the title in the transport bar, shown only while this PC's player plays a stream (Lyrion's `remote`). Never for library files, never for other players. Click to record: it turns solid red, pulses and shows the elapsed time; click again to stop. Settings › Recording can hide it completely.
- **Capture:** inside our Squeezelite (`vendor/squeezelite/src/output_rec_win.c`, shared memory `Local\squeezelite-rec-<mac>`). The decoded audio about to play, before EQ and volume, goes through a ring to a writer thread that encodes 24-bit FLAC at the stream's own sample rate, so the audio thread never touches the disk. A rate change mid-recording continues in "(part 2)".
- **Files:** by default one folder per recording, `<folder>\<station> - yyyy-MM-dd, HH.mm.ss\NN <artist> - <title>.flac` (numbered in order; ALBUM = station and start time, plus TRACKNUMBER, so each recording is one album). Settings › Recording can instead use one folder per station, `<folder>\<station>\<yyyy-MM-dd HH.mm> <artist> - <title>.flac` (ALBUM = station). Also tagged TITLE, ARTIST, DATE and COMMENT. Default folder `Music\WinSqueeze recordings`. With "new file per song" (default on), each song change the station announces starts a new file (timing follows Lyrion's delayed title change, so cuts are approximate).
- **Ends** when you click stop, the player leaves the stream for a library file or stops, or WinSqueeze quits (the file is finished cleanly). A Squeezelite restart (device change) finishes the file and carries on in a new one.
- A toast says where the files went. The UI and Settings say recordings are for personal use only.

## Play history
Every play is kept for good in history.db (per server profile), once it has run 20 seconds: library tracks by id, radio and other streams by URL, so an episode can be played again. The History section lists plays by day, newest first. An album played through (3 or more of its tracks in a row) is one block; each radio programme is its own row with Play again; everything else groups by hour. Selection actions: Play now / next / add to end, Track info, Remove from history; Clear history asks first.

## Offline downloads

Albums (or any selection: artists, genres, folders, tracks, playlists) can be downloaded to this PC and played without the server.

- **Where:** **Download** in the selection bar (any list, any number of items) and on the album page. Covers of downloaded albums carry a small badge (outlined when only some tracks are on this PC).
- **Fetching:** the shell downloads each track's original file from Lyrion (`/music/<track id>/download`), lossless and untouched, two at a time in the order asked. A partial file resumes with an HTTP Range request; a dropped connection puts the track back in the queue and retries a minute later. The queue survives restarts. Covers are cached at every size the UI uses.
- **Storage:** `<data folder>\Offline\<profile>\<album artist>\<album>\<disc-track title>.<ext>`, with a `downloads.db` per server profile recording each track's album, tags, size and state. Downloads follow Lyrion track ids, re-linked by file URL after a full rescan.
- **Downloaded** (sidebar section): what's still downloading sits on top, one row per album with a progress bar and ✕ (cancel the rest of the album); expanding a row lists its tracks, each with its own ✕. Retry failed and Cancel all are in its header. Below, the downloaded albums as a normal album grid with the usual Sort / Group / View, where the selection bar offers **Remove download** (deletes the files). The footer shows tracks and size on this PC and opens the folder.
- **Offline playback:** Squeezelite only plays what a server streams to it, so the shell carries a small offline server: it speaks slimproto to our Squeezelite on 127.0.0.1, streams the downloaded files over local HTTP and answers the player commands the UI uses (status, play, pause, seek, volume, next/previous, queue add/insert/move/delete/clear, repeat) with Lyrion-shaped replies. Playback stays bit-perfect through the same Squeezelite, EQ, meters and recording; tracks follow each other gaplessly (the next stream is sent when the decoder finishes, never before the previous track has started). Seeking is frame-exact for FLAC, sample-exact for WAV/AIFF and approximate for MP3; other formats restart from the beginning.
- **Going offline:** automatically when five player polls in a row fail and something is downloaded (tray note "Playing offline"), or by hand from the player picker (**Play offline**). The downloaded part of the current queue carries on at the same spot. The app opens Downloaded; browsing still works from the local index, and only downloaded tracks can be queued.
- **Going back online:** checked every 30 seconds. If nothing is playing it goes back by itself; while playing it offers "The server is back · Go online" instead of interrupting.

## Output device selection

1. **Picker in the app:** a dropdown in the transport bar's device indicator, in settings (Audio tab) and in the tray menu.
2. **Friendly names, grouped by driver:** WASAPI, ASIO (if present), DirectSound/MME. Names are shown as Windows gives them, with no exclusive label; whether exclusive is in use shows in the player status.
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
- Right-click menu: Play/Pause, Next, Previous, New playlist, Output device ▸, Player ▸, Sleep ▸, Window mode ▸, Skin ▸, Settings, Quit.
- Mouse wheel over the icon: volume.
- Tooltip: current track and artist.

**Other Squeezelite instances:** at launch, WinSqueeze looks for any other Squeezelite running on the PC, even one whose exe has been renamed. Candidates are processes connected to a server's player port (TCP 3483) or with "squeezelite" in their name, confirmed by Squeezelite's own banner text inside the exe. For each one it offers to stop it, and if a Startup-folder shortcut or HKCU Run entry launches it at login, to switch that off (moved aside, never deleted). Settings › General sets this to Ask (default), Always stop or Leave it running.

**Playback at startup:** Lyrion restarts whatever a player was playing when it reconnects, so Squeezelite starts held (playing silence, `-H`) and WinSqueeze first asks the server what this player was doing. Playback carries on only if Settings › General › "Resume playing on startup" is ticked (off by default) and the server was still playing to this player; otherwise WinSqueeze pauses it (queue and position kept) before the hold lifts. A restart for an update always carries on.

**Close (X):** quits (default). Settings › General › "Close (X) keeps WinSqueeze running in the tray" makes it hide to tray and keep playing instead; the first time, a one-off toast explains this. Quitting always ends the process (a watchdog exits after 5 s if anything hangs), so its WebView2 and Squeezelite processes go too.

**Single instance:** launching WinSqueeze while it runs (even hidden in the tray) brings the running window to the front. If the running copy doesn't answer within 8 s (hung, or stuck quitting), the new launch ends it and starts normally.

**Media integration:**

- Keyboard media keys (play/pause, next, previous) control WinSqueeze.
- Windows media overlay (System Media Transport Controls): track, artist and art in the volume flyout and lock screen, with working controls.
- Taskbar thumbnail buttons: previous, play/pause, next.
- Track-change toast notifications: available, off by default.

**Keyboard shortcuts (rebindable in settings):** Space play/pause; Ctrl+Left/Right previous/next; Ctrl+Up/Down volume; Ctrl+F search (Down from the search box moves into the results; arrows move between them, by row in grids; Enter plays or opens; Esc goes back to the search box); Ctrl+N new playlist; Ctrl+1/2/3 window mode; Ctrl+, settings; Ctrl+Q quit. Optional global hotkeys, off by default.

**Esc closes one layer per press, innermost first:** an open menu, dropdown, popover or drag; then sidebar editing; info panels; full-window meters; a multi-selection; a toast; the search text; and only then goes back a level.

## Damaged files

Squeezelite logs each decode error (for example a FLAC frame it can't read). WinSqueeze ties the errors to the track they belong to (decoding runs ahead of playback, so "codec open" and "track start" log lines are matched up), and only counts library files, not radio. On the first error it says the file has damaged frames and is playing past them, and Now playing shows the same note. It never skips a damaged file: the decoder resyncs past bad frames and plays as much of the file as it can. The first error message is kept with the file. A file the decoder rejects outright is reported as unplayable. Damaged files are remembered (`damaged-files.json` in the data folder): track lists mark them with ⚠, and Library health lists them with their server paths and first error, with Copy list and Clear list buttons. There is no up-front scan of the library.

## Library health

A sidebar section (and a button in Settings › Library & cache) listing what in the library needs fixing in a tagger. WinSqueeze never changes files or the server. Checks run on the local index, per album folder (one folder = one CD), or per tag album when the library has no folders:

- **Missing cover art:** no cover in the files or folder, or the server only has its placeholder (each cover is fetched once at list size, which also warms the art cache).
- **Missing composer:** albums with tracks that have no composer tag, with how many. Shows classical albums (a conductor or orchestra credit, or a classical genre) by default, or all albums.
- **Duplicate albums:** folders with identical track lists, or the same artist and title with at least half the tracks in common.
- **Tag problems:** no artist at all; album artist missing on some tracks, differing between tracks, or missing on a multi-artist album; a folder the server reads as several albums; missing, repeated or skipped track numbers (per disc); no year, an odd year (before 1900 or in the future) or mixed years.
- **Name problems:** two names (same role) that only ever appear together where one is a single word or together they make a name already in the library ("Earth" + "Wind & Fire"); one person spelled two ways (same words in another order or punctuation).
- **Damaged files** (see Damaged files).

Each row opens its album (a damaged file opens its album at that track; a person opens their page). Summary tiles jump to each section; each section has Copy list (artist — title — folder — problem). The report is cached per profile (`health.json`) until the index changes; Rescan asks the server for changes, checks missing covers again and rebuilds the report.

## Diagnostics

Unhandled errors, background task errors, WebView2 renderer failures and UI-thread freezes over 8 seconds (hang watchdog) are written to `logs\crash.log` with the version and the last shell activity. The next launch shows a tip pointing to it. Squeezelite's log keeps at most 50 lines a second, so a retry loop can't flood it.

**Copy diagnostics** (Settings › About) copies a plain-text report to the clipboard: version and channel, Windows, server address, player state, output device, and the end of crash.log and squeezelite.log. No passwords.

**Send diagnostics…** (next to it) shows the same report in an editable box with server and other URL host names, PC, player and Windows user names, IP and MAC addresses and folder paths replaced by `<server>`, `<host>`, `<pc>`, `<player>`, `<user>`, `<ip>`, `<mac>`, `<path>`. Copy and open GitHub Discussions copies it and opens a new Discussion (General) with a "What happened?" prompt and the report filled in (or a note to paste it, when the link would be too long). Nothing is sent until the user posts it there. There is no telemetry.

## Privacy

Settings › About says: "The developer takes privacy seriously. Apart from update checks and the Artist, Album and Lyrics lookups (which you can turn off in Now playing views), WinSqueeze sends nothing outside your network." An expander, What leaves your network, tables each case: update checks (GitHub, the release list, no ID or usage data), Artist / Album / Lyrics (MusicBrainz, Wikipedia, Wikidata, Cover Art Archive, LRCLIB, Last.fm only with the user's key; artist, album and track names) and Send diagnostics (GitHub Discussions, only when the user posts it). Keep both accurate whenever a feature starts talking to the internet.

## Server library

Settings › Server › Library starts Lyrion's own library scans and follows them, for when music was added or retagged.

- **Status card** (polled every 2 s while the page is open, from `serverstatus` and `rescanprogress`): while scanning, the current step (the server's own wording), a progress bar with done / total, the time so far, the file or folder being read, and the list of steps with a tick on finished ones; a Stop scan button (`abortscan`). For its first seconds a new scan still reports the previous scan's steps, all finished: that shows as "Starting the scan". Otherwise a tick, "Last library scan finished <date, time> (<how long ago>)" (a playlists-only scan doesn't change it) and the server's track, album and artist totals; a warning callout if the last scan failed.
- **Buttons:** Scan for changes (`rescan`), Playlists only (`rescan playlists`), Clear and rescan everything (`wipecache`, after a confirm saying every player sees a partial library until it finishes). An expandable table explains the three. Disabled while a scan runs, offline, or when the server can't be reached.
- When a scan it saw running finishes, WinSqueeze refreshes its own index straight away, then a callout lists the albums the server first saw after its previous scan ("This scan added 3 albums", up to six by name, "and N more") with Show in New music. It stays for the session while the server's last scan time is unchanged.
- This page is the home for later server-side library features. Settings › Library & cache stays for WinSqueeze's own index.

## Settings and servers

A separate Settings window with tabs, in standard Windows style, listed in a sidebar under section headings: Server (Servers, Library), Player (Audio, Playback, Now playing views, Recording), Library (Library & cache, Browsing), Look, App. Changes apply immediately (no OK/Apply). Each page is a column of cards with headings; explanations are info or warning callouts, longer detail sits in expandable panels (with tables where it helps, e.g. how names are split), and keyboard shortcuts are tables of keycaps. First run opens it straight on the Servers tab.

| Tab | Settings |
| --- | --- |
| General | Settings export / import (JSON; everything except passwords and other secrets); Start with Windows; resume playing on startup (off; when both are on, an amber bracket joins them with a warning that music may start as soon as the PC restarts); start minimised to tray; other Squeezelite at launch (ask / always stop / leave); minimise to tray when minimised; close (X) quits / keeps running in the tray (off); remember window position; language |
| Servers | Server finder (auto-discovery); profiles list (name, address, port, optional username/password); default profile; test connection |
| Library (server) | Scan status and progress; Scan for changes; Playlists only; Clear and rescan everything (see Server library) |
| Audio | Output device; driver type; exclusive mode on/off; volume mode (fixed / digital); player name (defaults to the computer name); extra Squeezelite arguments (advanced); fidelity warnings |
| Now playing views | Tabs: Up next first and fixed (ticked, locked), then Artist, Album, Lyrics, each with a tickbox, a drag grip and up/down arrows to set the order; Timeline as a view of Artist (on); Credits as a view of Album (on). Sources: one list per tab, each source with a tickbox and drag grip (the plugin shows whether the server has it; Last.fm needs a key). Last.fm API key (stored encrypted for this Windows user, never exported) |
| Playback | For the selected player (Lyrion player prefs, written only when changed): between tracks (nothing/gapless, crossfade, fade in, fade out, fade out and in), its length, smart (no crossfade within an album); ReplayGain (off, track, album, smart). On this PC these break bit-perfect, and the fidelity badge says so |
| Library & cache | Album source (tags / folders); contributor grouping (any credit / album artist text only); name separators; refresh interval; Refresh now; Rebuild cache; cache size and location; clear art cache; Library health (damaged file count, Open library health) |
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
