# WinSqueeze

WinSqueeze is a standalone Windows app that browses, controls and plays music from any Lyrion Music Server without touching the server. You launch it, browse your library, and it plays bit-perfect through your chosen output. You never open the server's web UI.

**Introduction:** the [Introducing WinSqueeze](https://forums.lyrion.org/forum/user-forums/3rd-party-software/1841197-introducing-winsqueeze) post on the Lyrion forums introduces the software, with screenshots and discussion.

## A synchronising client

WinSqueeze keeps a complete local mirror of the server's library (tracks, tags, contributors, folders, cover art) and keeps it in sync automatically. Every Lyrion controller we know of asks the server live for each screen. WinSqueeze browses and searches its own synced copy, so it is instant over any connection, and it can offer views the server doesn't, such as folder-grouped search. The server stays the single source of truth; WinSqueeze only ever follows it.

No server plugins, no skin switching, no server settings changed. WinSqueeze only reads the library through Lyrion's standard JSON-RPC API and controls playback like any other Squeezebox player.

## Who it's for

- **Audiophile listeners**, especially of jazz and classical, who want bit-perfect playback and browsing that goes beyond artist and album, such as by composer and by performer, conductor or ensemble.
- **Careful archivists** who have built their collection over many years so that each folder on disk is one CD, LP or album. WinSqueeze treats that folder structure as a first-class view of the library.

## Signature feature: search by tag, grouped by folder

Search and browse use the ID3/Vorbis tags (composer, performer, genre, artist), but the results are grouped by music folder, so each hit shows up as the physical CD or LP it belongs to.

## Download

Get the latest stable release from [Releases](https://github.com/hazymat/winsqueeze/releases/latest): `WinSqueeze-Setup.exe` installs per user (no admin needed), `WinSqueeze-Portable.exe` runs from anywhere. WinSqueeze updates itself. Builds are not code-signed yet, so Windows SmartScreen may warn on first run (More info › Run anyway).

Questions, ideas and bug reports are welcome in [Discussions](https://github.com/hazymat/winsqueeze/discussions). See [SPEC.md](SPEC.md) for the full specification.

## Licence

GPLv3. Bundles [Squeezelite](https://github.com/ralph-irving/squeezelite) (GPLv3); see `vendor/squeezelite`.
