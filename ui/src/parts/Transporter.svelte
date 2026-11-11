<script lang="ts">
  import { t } from '../lib/i18n'
  // Transporter mode (SPEC.md › Transporter mode): the whole window becomes a two-display network player, black glass over a silver key strip, with a TransNav knob
  import { app, meters, currentTrack, cycleMeterView, readSetting, writeSetting } from '../state.svelte'
  import { command, sendToShell, query, live, request } from '../bridge'
  import { resolveTracks } from '../lib/browseActions'
  import { vfdGlyphs, vfdTop } from './vfdFont'
  const stageWidth = 1400, stageHeight = 196
  let windowWidth = $state(stageWidth), windowHeight = $state(stageHeight)
  const stageZoom = $derived(Math.min(windowWidth / stageWidth, windowHeight / stageHeight))
  const status = $derived(app.nowPlaying)
  const track = $derived(currentTrack())
  const playing = $derived(status?.mode === 'play')
  const duration = $derived(Number(status?.duration ?? track?.duration ?? 0))
  let now = $state(Date.now())
  $effect(() => { const ticker = setInterval(() => now = Date.now(), 100); return () => clearInterval(ticker) })
  const position = $derived(Math.min(duration || Infinity, Number(status?.time ?? 0) + (playing ? (now - app.receivedAt) / 1000 : 0)))
  // Volume follows the knob at once, the player catches up
  let turningVolume = $state<number | null>(null), lastVolumeSent = 0, volumeReleaseTimer = 0
  const volume = $derived(turningVolume ?? Math.abs(Number(status?.['mixer volume'] ?? 0)))
  function nudgeVolume(steps: number) {
    const level = Math.max(0, Math.min(100, Math.round(volume + steps * 2.5))); turningVolume = level
    if (Date.now() - lastVolumeSent > 60 || level === 0 || level === 100) { lastVolumeSent = Date.now(); command('mixer', 'volume', level) }
    clearTimeout(volumeReleaseTimer); volumeReleaseTimer = window.setTimeout(() => { if (turningVolume !== null) command('mixer', 'volume', turningVolume); turningVolume = null }, 400)
  }

  // What the unit is doing: the left display's screen, the right display's visual, the font size, the brightness (kept between visits)
  type Entry = { name: string, detail?: string, kind?: string, key?: string, open?: () => Promise<Entry[]> }
  type Level = { title: string, entries: Entry[], index: number }
  let screen = $state<'playing' | 'browse' | 'search' | 'volume' | 'standby'>('playing'), nowPlayingFormat = $state(Number(readSetting('transporterFormat', '0')) % 3)
  let visual = $state(readSetting('transporterVisual', 'spectrum') === 'vu' ? 'vu' : 'spectrum'), large = $state(readSetting('transporterSize', 'large') !== 'small'), brightness = $state(Number(readSetting('transporterBrightness', '3')) % 4)
  $effect(() => { writeSetting('transporterFormat', String(nowPlayingFormat)); writeSetting('transporterVisual', visual); writeSetting('transporterSize', large ? 'large' : 'small'); writeSetting('transporterBrightness', String(brightness)) })
  let levels = $state<Level[]>([]), loading = $state(false), searchText = $state(''), flash = $state({ text: '', until: 0 })
  const level = $derived(levels[levels.length - 1])
  const entry = $derived(level?.entries[level.index])
  let volumeTimer = 0
  function showVolume() { if (screen !== 'volume') screenBeforeVolume = screen === 'standby' ? 'playing' : screen; screen = 'volume'; clearTimeout(volumeTimer); volumeTimer = window.setTimeout(() => { if (screen === 'volume') screen = screenBeforeVolume }, 3000) }
  let screenBeforeVolume: typeof screen = 'playing'
  const say = (text: string) => flash = { text, until: Date.now() + 1800 }

  // Library menus on the display, as the unit's own: each level a list the knob scrolls through
  const albumEntries = (albums: any[]): Entry[] => albums.map(album => ({ name: String(album.title ?? album.name ?? ''), detail: String(album.artist ?? album.detail ?? ''), kind: 'album', key: String(album.key), open: () => albumTracks(String(album.key)) }))
  async function albumTracks(key: string): Promise<Entry[]> { const result = await query('album', { key }); return (result?.tracks ?? []).map((item: any) => ({ name: String(item.title ?? ''), detail: String(item.artist || item.albumArtist || ''), kind: 'track', key: String(item.id) })) }
  const artistEntry = (artist: any): Entry => ({ name: String(artist.name ?? ''), kind: 'artist', key: String(artist.key), open: async () => { const result = await query('artistAlbums', { key: String(artist.key) }); return albumEntries((result?.own ?? []).concat(result?.appearsOn ?? [])) } })
  const rootEntries = (): Entry[] => [
    { name: t('Artists'), open: async () => ((await query('artists')) ?? []).map(artistEntry) },
    { name: t('Albums'), open: async () => albumEntries((await query('albums'))?.own ?? []) },
    { name: t('Genres'), open: async () => ((await query('genres')) ?? []).map((genre: any) => ({ name: String(genre.name ?? ''), kind: 'genre', key: String(genre.key), open: async () => albumEntries((await query('genreAlbums', { key: String(genre.key) }))?.own ?? []) })) },
    { name: t('New music'), open: async () => albumEntries((await query('newAlbums'))?.own ?? []) },
    { name: t('Playlists'), open: async () => ((await live(['playlists', 0, 500]))?.playlists_loop ?? []).map((saved: any) => ({ name: String(saved.playlist ?? ''), kind: 'playlist', key: String(saved.id), open: async () => (await resolveTracks('playlist', [String(saved.id)])).map(item => ({ name: item.title, detail: item.artist, kind: 'track', key: String(item.id) })) })) }
  ]
  async function openLevel(title: string, load: () => Promise<Entry[]>) {
    if (loading) return
    loading = true
    try { const entries = await load(); levels = levels.concat([{ title, entries: entries.length ? entries : [{ name: t('Nothing here.') }], index: 0 }]); screen = 'browse' } catch { say(t('Nothing here.')) }
    loading = false
  }
  function browse() { levels = [{ title: t('Browse'), entries: rootEntries(), index: 0 }]; screen = 'browse' }
  function back() { if (screen === 'browse' && levels.length > 1) levels = levels.slice(0, -1); else screen = 'playing' }
  // Song details: pressing the knob in Now playing lists the song's facts; its artist and album open like any menu
  async function showDetails() {
    if (!track) return
    const place = await query('trackPlace', { id: String(track.id ?? '') }).catch(() => null), rate = Number(track.samplerate ?? 0), depth = Number(track.samplesize ?? 0)
    const facts: Entry[] = [{ name: String(track.title ?? '') }]
    if (track.artist) facts.push(place?.artist ? artistEntry({ name: track.artist, key: place.artist }) : { name: String(track.artist) })
    if (track.album) facts.push(place?.album ? { name: String(track.album), detail: String(place.artistName ?? ''), kind: 'album', key: String(place.album), open: () => albumTracks(String(place.album)) } : { name: String(track.album) })
    if (track.year) facts.push({ name: String(track.year) })
    if (track.genre) facts.push({ name: String(track.genre) })
    const format = [String(track.type ?? '').toUpperCase(), rate ? `${(rate / 1000).toFixed(rate % 1000 ? 1 : 0)} kHz` : '', depth ? `${depth} bit` : ''].filter(Boolean).join(' · ')
    if (format) facts.push({ name: format })
    levels = [{ title: t('Details'), entries: facts, index: 0 }]; screen = 'browse'
  }
  // PLAY on a song inside a list plays the list from that song; anything else plays all its songs. ADD adds to the end
  async function playEntry(adding: boolean) {
    if (!entry?.kind || !entry.key) return
    const songs = level.entries.filter(item => item.kind === 'track')
    if (entry.kind === 'track' && !adding && songs.length > 1) sendToShell({ type: 'playTracks', ids: songs.map(item => Number(item.key)), mode: 'load', start: songs.indexOf(entry) })
    else { const items = await resolveTracks(entry.kind, [entry.key]); if (!items.length) { say(t('Nothing to play there')); return } sendToShell({ type: 'playTracks', ids: items.map(item => item.id), mode: adding ? 'add' : 'load', start: 0 }) }
    if (adding) say(t('Added {items} to Now playing', { items: `“${entry.name}”` })); else screen = 'playing'
  }
  // SEARCH: type on the keyboard (or turn the knob through the letters), Enter or the knob runs it
  const letters = ' ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'
  async function runSearch() {
    const text = searchText.trim(); if (!text) return
    levels = []
    await openLevel(`“${text}”`, async () => {
      const result = await query('search', { text })
      const songs = (result?.trackGroups ?? []).flatMap((group: any) => group.tracks.map((item: any) => ({ name: String(item.title ?? ''), detail: String(group.album?.title ?? ''), kind: 'track', key: String(item.id) })))
      return (result?.artists ?? []).map(artistEntry).concat(albumEntries(result?.albums ?? []), songs)
    })
  }

  function press(name: string) {
    if (name === 'power') { if (screen === 'standby') screen = 'playing'; else { screen = 'standby'; if (playing) command('pause', '1') } return }
    if (screen === 'standby') screen = 'playing' // any key wakes the display
    if (name === 'nowplaying') { if (screen === 'playing') nowPlayingFormat = (nowPlayingFormat + 1) % 3; screen = 'playing' }
    else if (name === 'browse') browse()
    else if (name === 'search') { searchText = ''; screen = 'search' }
    else if (name === 'back') back()
    else if (name === 'size') large = !large
    else if (name === 'brightness') brightness = (brightness + 3) % 4
    else if (name === 'visual') visual = visual === 'spectrum' ? 'vu' : 'spectrum'
    else if (name === 'volume') { if (screen === 'volume') screen = screenBeforeVolume; else showVolume() }
    else if (name === 'play') { if (screen === 'browse' && entry?.kind) playEntry(false); else command('pause', '0') }
    else if (name === 'add') { if (screen === 'browse') playEntry(true) }
    else if (name === 'pause') command('pause', playing ? '1' : '0')
  }
  // REW and FWD: a click goes back to the start of the song (or the one before, if it has only just begun) or on to the next; holding scans
  let scanTimer = 0, scanStart = 0
  function startScan(event: PointerEvent, direction: number) { if (event.button !== 0) return; stopScan(); scanStart = window.setTimeout(() => { const hop = () => command('time', direction > 0 ? '+5' : '-5'); hop(); scanTimer = window.setInterval(hop, 450) }, 500) }
  function stopScan() { clearTimeout(scanStart); clearInterval(scanTimer); scanStart = 0; scanTimer = 0 }
  function clickSkip(direction: number) {
    if (scanTimer) { stopScan(); return } stopScan(); if (screen === 'standby') screen = 'playing'
    if (direction > 0) command('playlist', 'index', '+1'); else if (position > 4) command('time', 0); else command('playlist', 'index', '-1')
  }

  // TransNav: turn it (drag, the wheel or the arrow keys) to move through a list, the letters or the volume; press it to go in
  function turn(steps: number) {
    if (screen === 'standby') screen = 'playing'
    if (screen === 'browse' && level) level.index = Math.max(0, Math.min(level.entries.length - 1, level.index + steps))
    else if (screen === 'search') { const last = searchText.slice(-1).toUpperCase(), next = letters[(letters.indexOf(last) + steps + letters.length * 4) % letters.length]; searchText = (searchText.length ? searchText.slice(0, -1) : '') + next }
    else { nudgeVolume(steps); showVolume() }
  }
  function pressKnob() {
    if (screen === 'browse' && entry) { if (entry.open) openLevel(entry.name, entry.open); else if (entry.kind) playEntry(false) }
    else if (screen === 'search') { if (searchText.endsWith(' ') || !searchText) searchText += 'A'; else runSearch() }
    else if (screen === 'playing') showDetails()
    else screen = 'playing'
  }
  let knobGrab = null as null | { y: number, x: number, step: number, moved: boolean }
  function grabKnob(event: PointerEvent) { if (event.button !== 0) return; (event.currentTarget as Element).setPointerCapture(event.pointerId); knobGrab = { x: event.clientX, y: event.clientY, step: 0, moved: false } }
  function turnKnob(event: PointerEvent) {
    if (!knobGrab) return
    const travel = (event.clientX - knobGrab.x - (event.clientY - knobGrab.y)) / stageZoom, step = Math.round(travel / (screen === 'browse' ? 18 : 10))
    if (Math.abs(travel) > 4) knobGrab.moved = true
    if (step !== knobGrab.step) { turn(step - knobGrab.step); knobGrab.step = step }
  }
  function letGoKnob() { if (knobGrab && !knobGrab.moved) pressKnob(); knobGrab = null }
  const wheelKnob = (event: WheelEvent) => { event.preventDefault(); const down = event.deltaY > 0; turn(screen === 'browse' ? (down ? 1 : -1) : (down ? -1 : 1)) }
  // Keyboard: typing goes into SEARCH; the arrows, Enter and Backspace work the menus; Esc backs out, then leaves
  function onKey(event: KeyboardEvent) {
    if (event.ctrlKey || event.altKey || event.metaKey) return
    const handled = () => { event.preventDefault(); event.stopPropagation() }
    if (screen === 'search') {
      if (event.key.length === 1) { searchText += event.key; handled() }
      else if (event.key === 'Backspace') { searchText = searchText.slice(0, -1); handled() }
      else if (event.key === 'Enter') { runSearch(); handled() }
      else if (event.key === 'Escape') { screen = 'playing'; handled() }
      return
    }
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') { if (screen === 'browse') turn(event.key === 'ArrowDown' ? 1 : -1); else turn(event.key === 'ArrowUp' ? 1 : -1); handled() }
    else if (event.key === 'ArrowRight' || event.key === 'Enter') { pressKnob(); handled() }
    else if (event.key === 'ArrowLeft' || event.key === 'Backspace') { back(); handled() }
    else if (event.key === ' ') { press('pause'); handled() }
    else if (event.key === 'Escape' && screen !== 'playing') { screen = 'playing'; handled() }
  }
  $effect(() => { window.addEventListener('keydown', onKey, true); return () => window.removeEventListener('keydown', onKey, true) })
  // Bare panel moves the window; right-click (or Esc) goes back to the normal player, the end of the cycle
  function pressPanel(event: MouseEvent) { if (event.button === 0 && !(event.target as HTMLElement).closest('button, .knob')) sendToShell({ type: 'dragWindow' }) }

  // The two displays: 320 x 32 dot graphic VFDs. Each frame is drawn into a 320 x 32 buffer, then every lit dot is painted as a dot on the glass
  const dotsWide = 320, dotsHigh = 32, pitch = 1.5, displayWidth = dotsWide * pitch, displayHeight = dotsHigh * pitch
  let leftCanvas = $state<HTMLCanvasElement>(), rightCanvas = $state<HTMLCanvasElement>()
  const leftBuffer = document.createElement('canvas'), rightBuffer = document.createElement('canvas')
  leftBuffer.width = rightBuffer.width = dotsWide; leftBuffer.height = rightBuffer.height = dotsHigh
  // 5x7 small font (the unit's top line) and a 3x5 figure font for the VU scales
  const font: Record<string, number[]> = { A: [14,17,17,17,31,17,17], B: [30,17,17,30,17,17,30], C: [14,17,16,16,16,17,14], D: [28,18,17,17,17,18,28], E: [31,16,16,30,16,16,31], F: [31,16,16,30,16,16,16], G: [14,17,16,23,17,17,15], H: [17,17,17,31,17,17,17], I: [14,4,4,4,4,4,14], J: [7,2,2,2,2,18,12], K: [17,18,20,24,20,18,17], L: [16,16,16,16,16,16,31], M: [17,27,21,21,17,17,17], N: [17,17,25,21,19,17,17], O: [14,17,17,17,17,17,14], P: [30,17,17,30,16,16,16], Q: [14,17,17,17,21,18,13], R: [30,17,17,30,20,18,17], S: [15,16,16,14,1,1,30], T: [31,4,4,4,4,4,4], U: [17,17,17,17,17,17,14], V: [17,17,17,17,17,10,4], W: [17,17,17,21,21,21,10], X: [17,17,10,4,10,17,17], Y: [17,17,17,10,4,4,4], Z: [31,1,2,4,8,16,31], '0': [14,17,19,21,25,17,14], '1': [4,12,4,4,4,4,14], '2': [14,17,1,2,4,8,31], '3': [31,2,4,2,1,17,14], '4': [2,6,10,18,31,2,2], '5': [31,16,30,1,1,17,14], '6': [6,8,16,30,17,17,14], '7': [31,1,2,4,8,8,8], '8': [14,17,17,14,17,17,14], '9': [14,17,17,15,1,2,12], ':': [0,12,12,0,12,12,0], '.': [0,0,0,0,0,12,12], ',': [0,0,0,0,12,4,8], '-': [0,0,0,31,0,0,0], "'": [12,4,8,0,0,0,0], '"': [10,10,0,0,0,0,0], '“': [10,10,0,0,0,0,0], '”': [10,10,0,0,0,0,0], '/': [0,1,2,4,8,16,0], '·': [0,0,0,12,12,0,0], '(': [2,4,8,8,8,4,2], ')': [8,4,2,2,2,4,8], '&': [12,18,20,8,21,18,13], '!': [4,4,4,4,4,0,4], '?': [14,17,1,2,4,0,4], '+': [0,4,4,31,4,4,0], '#': [10,10,31,10,31,10,10], ' ': [0,0,0,0,0,0,0] }
  const tiny: Record<string, number[]> = { '0': [7,5,5,5,7], '1': [2,6,2,2,7], '2': [7,1,7,4,7], '3': [7,1,3,1,7], '5': [7,4,7,1,7], '7': [7,1,1,2,2], '+': [0,2,7,2,0], '-': [0,0,7,0,0], V: [5,5,5,5,2], U: [5,5,5,5,7] }
  const plain = (text: string) => text.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[’‘]/g, "'").replace(/[–—]/g, '-').toUpperCase()
  const canvasFont = (size: number) => `600 ${size}px "Segoe UI", "Yu Gothic UI", "Microsoft YaHei UI", Inter, Arial, sans-serif`
  // Small line: the 5x7 font; text it can't draw (Cyrillic, Japanese…) falls back to a small proportional font
  function smallText(context: CanvasRenderingContext2D, text: string, left: number, top: number, room = dotsWide) {
    const shown = plain(text)
    if (!Array.from(shown).every(character => character in font)) { context.save(); context.beginPath(); context.rect(left, 0, room, dotsHigh); context.clip(); context.font = canvasFont(9.5); context.textAlign = 'left'; context.fillText(text.toUpperCase(), left, top + 7.5); context.restore(); return }
    for (const [cell, character] of Array.from(shown).entries()) { if ((cell + 1) * 6 > room) break; const rows = font[character]; for (let row = 0; row < 7; row++) for (let column = 0; column < 5; column++) if (rows[row] & (16 >> column)) context.fillRect(left + cell * 6 + column, top + row, 1, 1) }
  }
  const smallWidth = (context: CanvasRenderingContext2D, text: string) => { const shown = plain(text); if (Array.from(shown).every(character => character in font)) return shown.length * 6 - 1; context.font = canvasFont(9.5); return Math.ceil(context.measureText(text.toUpperCase()).width) }
  function tinyText(context: CanvasRenderingContext2D, text: string, centreX: number, top: number) { const left = Math.round(centreX - (text.length * 4 - 1) / 2); for (const [cell, character] of Array.from(text).entries()) { const rows = tiny[character] ?? [0,0,0,0,0]; for (let row = 0; row < 5; row++) for (let column = 0; column < 3; column++) if (rows[row] & (4 >> column)) context.fillRect(left + cell * 4 + column, top + row, 1, 1) } }
  // Big line: a dot font with every letter drawn on the grid, like the unit's own (tools/vfd_font.py); text it can't draw (Japanese, Chinese…) falls back to a proportional font snapped to dots. Long lines scroll after a pause
  const dotted = (text: string) => text.replace(/[\u00a0\t]/g, ' ')
  // The real unit's font when the shell has it (Lyrion's standard.2, downloaded on first use: app/Shell/TransporterFont.cs); letters it lacks come from the bundled dot font
  let dotFont = $state({ top: vfdTop, gap: 1, space: 4, glyphs: vfdGlyphs })
  request({ type: 'transporterFont' }).then((real: any) => { if (real?.glyphs) dotFont = { top: real.top, gap: real.gap, space: real.space, glyphs: Object.fromEntries(Object.entries(real.glyphs as Record<string, string>).map(([character, columns]) => [character, columns.split(',').map(column => parseInt(column, 16))])) } }).catch(() => {})
  const glyphOf = (character: string) => dotFont.glyphs[character] ?? vfdGlyphs[character]
  const dotFontCovers = (text: string) => Array.from(dotted(text)).every(character => character === ' ' || glyphOf(character))
  const dotWidth = (text: string) => Array.from(dotted(text)).reduce((width, character) => width + (character === ' ' ? dotFont.space : glyphOf(character).length) + dotFont.gap, 0) - dotFont.gap
  function dotText(context: CanvasRenderingContext2D, text: string, left: number, clipLeft: number, clipRight: number) {
    let x = left
    for (const character of dotted(text)) {
      if (character === ' ') { x += dotFont.space + dotFont.gap; continue }
      const columns = glyphOf(character), top = character in dotFont.glyphs ? dotFont.top : vfdTop
      for (const [index, column] of columns.entries()) { const at = x + index; if (at >= clipLeft && at < clipRight) for (let row = 0; column >> row; row++) if (column & (1 << row)) context.fillRect(at, top + row, 1, 1) }
      x += columns.length + dotFont.gap
    }
  }
  const bigWidth = (context: CanvasRenderingContext2D, text: string, size: number) => { if (dotFontCovers(text)) return dotWidth(text); context.font = canvasFont(size); return context.measureText(text).width }
  function bigText(context: CanvasRenderingContext2D, text: string, left: number, baseline: number, size: number, room: number) {
    const width = bigWidth(context, text, size), scrolling = width > room
    const travel = Math.round(width + 40), offset = scrolling ? Math.round(Math.max(0, ((now / 1000 * 28) % (travel + 90)) - 90)) : 0
    if (dotFontCovers(text)) { dotText(context, text, left - offset, left, left + room); if (scrolling) dotText(context, text, left - offset + travel, left, left + room); return }
    context.font = canvasFont(size); context.textAlign = 'left'
    if (!scrolling) { context.fillText(text, left, baseline); return }
    context.save(); context.beginPath(); context.rect(left, 0, room, dotsHigh); context.clip(); context.fillText(text, left - offset, baseline); context.fillText(text, left - offset + travel, baseline); context.restore()
  }
  const clock = (seconds: number) => { const whole = Math.max(0, Math.floor(seconds)); return String(Math.floor(whole / 60)).padStart(2, '0') + ':' + String(whole % 60).padStart(2, '0') }
  const arrow = (context: CanvasRenderingContext2D) => { context.beginPath(); context.moveTo(311, 15); context.lineTo(317, 21); context.lineTo(311, 27); context.closePath(); context.fill() }
  function drawLeft(context: CanvasRenderingContext2D) {
    if (screen === 'standby') { context.font = canvasFont(22); context.textAlign = 'center'; context.fillText(new Date(now).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }), dotsWide / 2, 25); return }
    if (flash.until > now) { smallText(context, String(track?.title ?? ''), 1, 1); bigText(context, flash.text, 1, 26, 19, 318); return }
    if (screen === 'volume') {
      smallText(context, `VOLUME (${Math.round(volume)})`, 1, 1)
      const steps = 40, filled = Math.round(volume / 100 * steps) // the unit's block bar: solid blocks up to the level, a dotted track after
      for (let step = 0; step < steps; step++) { const left = Math.round(2 + step * 7.9); if (step < filled) context.fillRect(left, 14, 6, 14); else context.fillRect(left, 20, 6, 1) }
      return
    }
    if (screen === 'search') {
      smallText(context, t('Search'), 1, 1)
      const width = bigWidth(context, searchText, 20), cursorLeft = Math.min(312, 1 + width + 2)
      bigText(context, searchText, Math.min(1, 310 - width), 26, 20, 318); if (Math.floor(now / 500) % 2) context.fillRect(Math.round(cursorLeft), 11, 2, 19)
      return
    }
    if (screen === 'browse' && level) {
      const counter = loading ? ' …' : ` (${level.index + 1} OF ${level.entries.length})`
      smallText(context, level.title, 1, 1, 318 - smallWidth(context, counter) - 6); smallText(context, counter, 318 - smallWidth(context, counter), 1)
      if (large) bigText(context, entry?.name ?? '', 1, 26, 20, entry?.open ? 304 : 318)
      else { smallText(context, entry?.name ?? '', 1, 12, 304); smallText(context, entry?.detail ?? '', 1, 23, 304) }
      if (entry?.open) arrow(context)
      return
    }
    // Now playing: top line with the queue place and (NOW PLAYING cycles) a progress bar, the elapsed time or the time left
    const index = Number(status?.playlist_cur_index ?? 0) + 1, total = Number(status?.playlist_tracks ?? 0)
    const heading = track ? `NOW PLAYING (${index} OF ${total})` : 'NOTHING PLAYING'
    smallText(context, heading, 1, 1)
    const elapsed = nowPlayingFormat === 2 ? '-' + clock(duration - position) : clock(position)
    if (track && duration) smallText(context, elapsed, dotsWide - smallWidth(context, elapsed) - 1, 1)
    if (track && duration && nowPlayingFormat === 0) { const barLeft = smallWidth(context, heading) + 6, barRight = dotsWide - smallWidth(context, elapsed) - 6, share = position / duration; context.strokeStyle = '#fff'; context.lineWidth = 1; context.strokeRect(barLeft + 0.5, 1.5, barRight - barLeft - 1, 5); context.fillRect(barLeft + 1, 2, Math.round((barRight - barLeft - 2) * share), 4) }
    const line = [track?.artist ?? track?.albumartist, track?.title].filter(Boolean).join(' - ')
    if (large) bigText(context, line || String(app.status?.message ?? ''), 1, 26, 20, playing ? 300 : 318)
    else { smallText(context, String(track?.title ?? ''), 1, 12); smallText(context, [track?.artist, track?.album].filter(Boolean).join(' - '), 1, 23) }
    if (playing && large) { context.font = '600 18px "Segoe UI Symbol", "DejaVu Sans", sans-serif'; context.textAlign = 'right'; context.fillText('♪', 318, 26) }
  }
  // Spectrum: both channels across the whole display, bass at the outer edges, thin bars with peak dots that hang, then fall
  const barsPerSide = 53
  const peakHold = Array(barsPerSide * 2).fill(0), peakAge = Array(barsPerSide * 2).fill(0)
  const resample = (values: number[], at: number) => { const place = at * (values.length - 1), low = Math.floor(place), high = Math.min(values.length - 1, low + 1); return (values[low] ?? 0) + ((values[high] ?? 0) - (values[low] ?? 0)) * (place - low) }
  function bandValues() {
    // The shell's 53 bands a channel; until they arrive, the 10 octaves stretched across both sides
    const wide = meters.wide.length === barsPerSide * 2, left = wide ? meters.wide.slice(0, barsPerSide) : meters.bands, right = wide ? meters.wide.slice(barsPerSide) : meters.bands
    return Array.from({ length: barsPerSide * 2 }, (unused, bar) => bar < barsPerSide ? resample(left, bar / (barsPerSide - 1)) : resample(right, (barsPerSide * 2 - 1 - bar) / (barsPerSide - 1)))
  }
  function drawSpectrum(context: CanvasRenderingContext2D) {
    bandValues().forEach((value, bar) => {
      const height = Math.max(1, Math.round(Math.min(1, value) * 29)), left = bar * 3 + (bar >= barsPerSide ? 2 : 0)
      context.fillRect(left, dotsHigh - height, 2, height)
      if (height >= peakHold[bar]) { peakHold[bar] = height; peakAge[bar] = 0 } else if (++peakAge[bar] > 18) peakHold[bar] = Math.max(height, peakHold[bar] - 0.35)
      if (peakHold[bar] > height + 1) context.fillRect(left, dotsHigh - Math.round(peakHold[bar]) - 1, 2, 1)
    })
  }
  // VU: two classic meter faces drawn dot by dot, figures over a flat arc that thickens into the red zone past 0 VU, the needle rising from below the display
  const vuScale = [[-20, -50], [-10, -32], [-7, -22], [-5, -13], [-3, -3], [-2, 3], [-1, 9], [0, 16], [1, 24], [2, 32], [3, 42]]
  const angleOf = (decibels: number) => { if (decibels <= vuScale[0][0]) return vuScale[0][1] - Math.min(6, (vuScale[0][0] - decibels) * 0.3); for (let mark = 1; mark < vuScale.length; mark++) if (decibels <= vuScale[mark][0]) { const [lowDb, lowAngle] = vuScale[mark - 1], [highDb, highAngle] = vuScale[mark]; return lowAngle + (highAngle - lowAngle) * (decibels - lowDb) / (highDb - lowDb) } return 46 }
  function drawVu(context: CanvasRenderingContext2D) {
    for (const channel of [0, 1]) {
      // The arc is a flattened ellipse, so the scale spans most of the face yet stays within the 32 rows
      const centreX = 80 + channel * 160, centreY = 54, radiusX = 76, radiusY = 42
      const arcPoint = (degrees: number, scale = 1) => { const radians = degrees * Math.PI / 180; return [centreX + Math.sin(radians) * radiusX * scale, centreY - Math.cos(radians) * radiusY * scale] }
      const dot = (x: number, y: number) => context.fillRect(Math.round(x), Math.round(y), 1, 1)
      for (let degrees = -50; degrees <= 42; degrees += 0.25) { const [x, y] = arcPoint(degrees); dot(x, y); if (degrees >= 16) { dot(x, y - 1); dot(x, y - 2) } }
      for (const [decibels, degrees] of vuScale) { const major = [-20, -10, -7, -5, -3, 0, 3].includes(decibels); for (let along = 1; along <= (major ? 3 : 2); along++) { const [x, y] = arcPoint(degrees, 1 + along * 0.035); dot(x, y) } }
      for (const [label, degrees] of [['20', -50], ['10', -32], ['7', -22], ['5', -13], ['3', -3], ['0', 16], ['3', 42]] as [string, number][]) { const [x, y] = arcPoint(degrees, 1.2); tinyText(context, label, x, Math.round(y - 3)) }
      tinyText(context, '-', centreX - 70, 1); tinyText(context, '+', centreX + 70, 1); tinyText(context, 'VU', centreX + 4, 21)
      // Needle: pivot well below the glass, so only its upper length shows
      const degrees = angleOf(meters.vu[channel] - meters.reference), [tipX, tipY] = arcPoint(degrees, 1.12)
      context.strokeStyle = '#fff'; context.lineWidth = 1.8; context.beginPath(); context.moveTo(centreX, 78); context.lineTo(tipX, tipY); context.stroke()
    }
  }
  const lit = '#5ef3dc'
  function paint(canvas: HTMLCanvasElement | undefined, buffer: HTMLCanvasElement, draw: (context: CanvasRenderingContext2D) => void, dim: number) {
    if (!canvas) return
    const bufferContext = buffer.getContext('2d', { willReadFrequently: true })!; bufferContext.clearRect(0, 0, dotsWide, dotsHigh); bufferContext.fillStyle = '#fff'; bufferContext.strokeStyle = '#fff'
    if (dim) draw(bufferContext)
    const pixels = bufferContext.getImageData(0, 0, dotsWide, dotsHigh).data
    const ratio = (window.devicePixelRatio || 1) * stageZoom
    if (canvas.width !== Math.round(displayWidth * ratio)) { canvas.width = Math.round(displayWidth * ratio); canvas.height = Math.round(displayHeight * ratio) }
    const context = canvas.getContext('2d')!; context.setTransform(ratio, 0, 0, ratio, 0, 0); context.clearRect(0, 0, displayWidth, displayHeight)
    context.fillStyle = lit; context.globalAlpha = dim
    for (let row = 0; row < dotsHigh; row++) for (let column = 0; column < dotsWide; column++) if (pixels[(row * dotsWide + column) * 4 + 3] > 110) context.fillRect(column * pitch + 0.1, row * pitch + 0.1, pitch * 0.82, pitch * 0.82)
  }
  // About 30 frames a second is plenty for a VFD; standby dims to the lowest step, as the unit does
  $effect(() => {
    let frame = 0, drawnAt = 0
    const step = (time: number) => {
      if (time - drawnAt > 30) { drawnAt = time; const dim = screen === 'standby' ? Math.min(0.3, [0, 0.3, 0.6, 1][brightness] || 0.3) : [0, 0.3, 0.6, 1][brightness]; paint(leftCanvas, leftBuffer, drawLeft, dim); paint(rightCanvas, rightBuffer, screen === 'standby' ? () => {} : visual === 'vu' ? drawVu : drawSpectrum, dim) }
      frame = requestAnimationFrame(step)
    }
    frame = requestAnimationFrame(step); return () => cancelAnimationFrame(frame)
  })

  // The key strip, as engraved on the unit: seven keys either side of the knob
  const leftKeys = ['POWER', 'NOW PLAYING', 'SEARCH', 'BROWSE', 'SIZE', 'BRIGHTNESS', 'BACK'], rightKeys = ['PLAY', 'ADD', 'REW', 'PAUSE', 'FWD', 'VOLUME', 'VISUAL']
  const keyName = (label: string) => label.toLowerCase().replace(' ', '')
</script>

<svelte:window bind:innerWidth={windowWidth} bind:innerHeight={windowHeight} />
{#snippet key(label: string)}
  {@const name = keyName(label)}
  <div class="key-cell"><span class="engraving">{label}</span>{#if name === 'rew' || name === 'fwd'}<button class="key" aria-label={label} onclick={() => clickSkip(name === 'fwd' ? 1 : -1)} onpointerdown={(event) => startScan(event, name === 'fwd' ? 1 : -1)} onpointerup={() => { if (scanStart && !scanTimer) stopScan() }} onpointerleave={() => { if (scanTimer) stopScan() }}></button>{:else}<button class="key" aria-label={label} onclick={() => press(name)}></button>{/if}</div>
{/snippet}
<!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
<div class="transporter" role="dialog" aria-label="Transporter" tabindex="-1" onmousedown={pressPanel} oncontextmenu={(event) => { event.preventDefault(); cycleMeterView() }}>
  <div class="stage" style:transform="translate(-50%, -50%) scale({stageZoom})">
    <div class="glass"><div class="sheen"></div></div>
    <div class="strip"></div>
    <div class="lip"></div>
    <div class="display left"><canvas bind:this={leftCanvas} style:width="{displayWidth}px" style:height="{displayHeight}px"></canvas></div>
    <div class="display right"><canvas bind:this={rightCanvas} style:width="{displayWidth}px" style:height="{displayHeight}px"></canvas></div>
    <div class="badge"><span>WIN</span><span>SQUEEZE</span></div>
    <div class="keys left">{#each leftKeys as label}{@render key(label)}{/each}</div>
    <div class="keys right">{#each rightKeys as label}{@render key(label)}{/each}</div>
    <div class="knob" role="slider" tabindex="-1" aria-label={t('Volume')} aria-valuenow={volume} title={t('Turn to move through lists or change the volume, click to choose')} onpointerdown={grabKnob} onpointermove={turnKnob} onpointerup={letGoKnob} onwheel={wheelKnob}><div class="face"></div></div>
    {#each ['left', 'right'] as side}<div class="handle {side}"></div>{/each}
  </div>
</div>

<style>
  .transporter { position: fixed; inset: 0; z-index: 90; overflow: hidden; outline: none; user-select: none; background: #050506; }
  :global(html.frameless:not(.maximised)) .transporter { border-radius: 8px; }
  .stage { position: absolute; left: 50%; top: 50%; width: 1400px; height: 196px; }
  /* Black glass over the displays: deep, glossy, a soft reflection across the top */
  .glass { position: absolute; left: 0; right: 0; top: 0; height: 120px; background: linear-gradient(180deg, #2a2d31 0, #0d0e10 3px, #060607 30%, #0a0b0d 100%); box-shadow: inset 0 1px 0 rgba(255,255,255,0.35); overflow: hidden; }
  .sheen { position: absolute; inset: 0; background: linear-gradient(172deg, rgba(255,255,255,0.09) 0%, rgba(255,255,255,0.03) 38%, rgba(255,255,255,0) 39%), radial-gradient(60% 120% at 50% -30%, rgba(255,255,255,0.06), transparent 70%); }
  /* Brushed silver key strip, a dark lip under it */
  .strip { position: absolute; left: 0; right: 0; top: 120px; height: 66px;
    background: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='1400' height='80'%3E%3Cfilter id='b'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.002 0.9' numOctaves='3' seed='3'/%3E%3CfeColorMatrix values='0 0 0 0 0.5 0 0 0 0 0.5 0 0 0 0 0.52 0 0 0 1 -0.3'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23b)'/%3E%3C/svg%3E") 0 0 / 1400px 80px, linear-gradient(100deg, rgba(255,255,255,0) 25%, rgba(255,255,255,0.35) 48%, rgba(255,255,255,0) 66%), linear-gradient(180deg, #f7f8f9 0, #c9cdd1 2px, #d7dadd 20%, #c4c8cc 70%, #a7acb1 100%);
    background-blend-mode: overlay, normal, normal; box-shadow: inset 0 1px 0 rgba(0,0,0,0.6), inset 0 2px 0 rgba(255,255,255,0.9), inset 0 -1px 1px rgba(0,0,0,0.3); }
  .lip { position: absolute; left: 0; right: 0; top: 186px; bottom: 0; background: linear-gradient(180deg, #1c1d20, #050506); }
  .display { position: absolute; top: 34px; width: 480px; height: 48px; padding: 10px 14px; margin: -10px -14px; border-radius: 4px; background: rgba(8, 12, 12, 0.55); box-shadow: inset 0 0 0 1px rgba(255,255,255,0.025); pointer-events: none; }
  .display.left { left: 112px; } .display.right { left: 818px; }
  .display canvas { display: block; filter: drop-shadow(0 0 1.6px rgba(80, 255, 225, 0.75)) drop-shadow(0 0 5px rgba(60, 240, 210, 0.25)); }
  /* Boxed wordmark above the knob */
  .badge { position: absolute; left: 700px; translate: -50% 0; top: 22px; display: flex; font: 700 10px/1 "Segoe UI", Inter, sans-serif; letter-spacing: 1px; color: #d6d9dc; pointer-events: none; }
  .badge span { padding: 3px 4px 2px; border: 1px solid #b9bdc1; } .badge span:first-child { background: #c9cdd1; color: #0a0b0c; border-right: 0; }
  .keys { position: absolute; top: 120px; height: 66px; width: 504px; display: flex; justify-content: space-between; }
  .keys.left { left: 104px; } .keys.right { left: 790px; }
  .key-cell { width: 66px; display: flex; flex-direction: column; align-items: center; gap: 7px; padding-top: 11px; }
  .engraving { font: 700 7.4px/1 "Segoe UI", Inter, Arial, sans-serif; letter-spacing: 0.3px; color: #2a2d31; text-shadow: 0 1px 0 rgba(255,255,255,0.6); white-space: nowrap; pointer-events: none; }
  /* Small domed silver keys sitting in a dark ring */
  .key { width: 22px; height: 22px; border: 0; border-radius: 50%; cursor: pointer; padding: 0;
    background: radial-gradient(circle at 42% 32%, #ffffff 0%, #e4e7ea 25%, #b5babf 60%, #8a9096 100%);
    box-shadow: 0 0 0 1.5px rgba(30,32,36,0.75), 0 0 0 3px rgba(255,255,255,0.5), 0 2px 3px 1px rgba(0,0,0,0.35), inset 0 -1px 2px rgba(0,0,0,0.25); }
  .key:active { translate: 0 1px; background: radial-gradient(circle at 45% 40%, #e8eaec 0%, #c3c7cb 45%, #8f959b 100%); box-shadow: 0 0 0 1.5px rgba(30,32,36,0.75), 0 0 0 3px rgba(255,255,255,0.5), inset 0 2px 3px rgba(0,0,0,0.35); }
  .key:focus-visible { outline: 2px solid rgba(40, 44, 50, 0.6); outline-offset: 4px; }
  /* TransNav: a turned aluminium knob, endless, so no pointer; spun sunburst face, bright chamfer */
  .knob { position: absolute; left: 700px; top: 124px; width: 112px; height: 112px; margin: -56px 0 0 -56px; border-radius: 50%; cursor: grab; touch-action: none; outline: none;
    background: linear-gradient(180deg, #f2f4f6, #8a9096 60%, #4a4e53); box-shadow: 0 10px 18px rgba(0,0,0,0.55), 0 3px 5px rgba(0,0,0,0.4), 0 0 0 1px rgba(0,0,0,0.6); }
  .knob:active { cursor: grabbing; }
  .face { position: absolute; inset: 5px; border-radius: 50%; background: conic-gradient(from 10deg, #eef0f2, #9aa0a6 12%, #fbfcfd 25%, #a3a9af 38%, #e6e9ec 50%, #959ba1 62%, #f8f9fa 75%, #a6acb2 88%, #eef0f2); box-shadow: inset 0 2px 1px rgba(255,255,255,0.9), inset 0 -2px 3px rgba(0,0,0,0.35); }
  .face::after { content: ''; position: absolute; inset: 0; border-radius: 50%; background: repeating-radial-gradient(circle at 50% 50%, rgba(0,0,0,0.05) 0 0.6px, rgba(255,255,255,0.06) 0.6px 1.4px); }
  /* Chrome handles either end: seen head-on they come straight out towards you, so only the front bar shows; its ends darken where the tube bends back into the case */
  .handle { position: absolute; top: 12px; bottom: 12px; width: 13px; border-radius: 6.5px; pointer-events: none;
    background: linear-gradient(180deg, rgba(0,0,0,0.55) 0, rgba(0,0,0,0) 9px, rgba(0,0,0,0) calc(100% - 9px), rgba(0,0,0,0.6) 100%), linear-gradient(90deg, #3a3d42 0%, #c9cdd2 22%, #ffffff 40%, #eef0f2 52%, #8d939a 78%, #2b2d31 100%);
    box-shadow: 3px 4px 6px rgba(0,0,0,0.55), 0 0 0 0.5px rgba(0,0,0,0.5); }
  .handle.left { left: 9px; } .handle.right { right: 9px; }
</style>
