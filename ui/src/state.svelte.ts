import { sendToShell, query, command } from './bridge'

export type View = { kind: string; key?: string; title?: string }
export type PlaylistItem = { id: number; title: string; artist: string; duration: number }

export const app = $state({
  status: null as any, nowPlaying: null as any, players: [] as any[], selectedPlayer: '', ownPlayer: '',
  library: { text: '', done: 0, total: 0 }, librarySettings: { AlbumSource: 'folders', ContributorGrouping: 'any' } as any, playback: { QueueEnd: 'stop', ContinueAdds: 'album', ContinueFrom: 'artist' } as Record<string, string>, hints: { hidden: false, dismissed: [] as string[] }, indexVersion: 0, receivedAt: 0,
  // Meter data from Squeezelite (~30 a second while playing): VU in dBFS, peak amplitude 0..1, spectrum bands 0..1
  levels: { vu: [-90, -90], peak: [0, 0], bands: Array(10).fill(0), at: 0 },
  meterSettings: { referenceDbfs: -18, autoReference: true }, // Settings › Meters
  window: { glass: false, frameless: false, maximised: false }
})
// Where you were browsing survives restarts (and updates)
const savedNav = (() => { try { return JSON.parse(readSetting('nav', '')) } catch { return null } })()
export const nav = $state({ section: (savedNav?.section ?? 'artists') as string, stack: (savedNav?.stack ?? []) as View[] })
$effect.root(() => { $effect(() => { writeSetting('nav', JSON.stringify({ section: nav.section, stack: nav.stack })) }) })
export const ui = $state({ search: '', scope: 'list' as 'list' | 'everything', expanded: false, meterStyle: readSetting('meterStyle', 'vu'), skin: readSetting('skin', 'auto'), coverAccent: readSetting('coverAccent', '') === '1', coverBackdrop: readSetting('coverBackdrop', '1') !== '0', accent: readSetting('accent', ''), glassTransparency: Number(readSetting('glassTransparency', '-1')), glassTintHue: Number(readSetting('glassTintHue', '0')), lookVersion: 0, pinned: false, metersFull: false, trackInfo: null as null | number[], toast: null as null | { text: string; undo?: () => unknown; redo?: () => unknown; ms: number; id: number } })
export const playlist = $state({ open: false, collapsed: false, name: 'Untitled playlist', items: [] as PlaylistItem[], history: [] as number[] })

export function readSetting(name: string, fallback: string) { try { return localStorage.getItem('ws.' + name) ?? fallback } catch { return fallback } }
export function writeSetting(name: string, value: string) { try { localStorage.setItem('ws.' + name, value) } catch { } }

// Forward history, like a browser: Back keeps what it left, any new navigation clears it
export const forwardHistory = $state({ views: [] as View[] })
export function openSection(section: string) { nav.section = section; nav.stack = []; ui.search = ''; forwardHistory.views = [] }
export function openView(view: View) { nav.stack = nav.stack.concat([view]); ui.search = ''; forwardHistory.views = [] }
export function back() { if (!nav.stack.length) return; forwardHistory.views = forwardHistory.views.concat([nav.stack[nav.stack.length - 1]]); nav.stack = nav.stack.slice(0, -1) }
export function forward() { if (!forwardHistory.views.length) return; nav.stack = nav.stack.concat([forwardHistory.views[forwardHistory.views.length - 1]]); forwardHistory.views = forwardHistory.views.slice(0, -1) }
export const currentView = () => nav.stack.length ? nav.stack[nav.stack.length - 1] : { kind: nav.section }

// Messages show in the action strip above the transport (ActionStrip.svelte), with a ring counting down their time
let toastTimer = 0, toastId = 0
// Undo history: every undoable action stays undoable (Ctrl+Z) after its bar has gone; actions that pass a redo can be redone (Ctrl+Y / Ctrl+Shift+Z)
type HistoryEntry = { text: string; undo: () => unknown; redo?: () => unknown }
const historyLimit = 100
export const history = $state({ undo: [] as HistoryEntry[], redo: [] as HistoryEntry[] })
function show(text: string, ms: number, undo?: () => unknown, redo?: () => unknown) { ui.toast = { text, undo, redo, ms, id: ++toastId }; clearTimeout(toastTimer); toastTimer = window.setTimeout(() => ui.toast = null, ms) }
// keep: false for undos that only make sense straight away (going back to a track you just left)
export function toast(text: string, undo?: () => unknown, undoMs = 6000, options: { redo?: () => unknown; keep?: boolean } = {}) {
  if (undo && options.keep !== false) { history.undo = history.undo.concat([{ text, undo, redo: options.redo }]).slice(-historyLimit); history.redo = []; show(text, undoMs, undoLast) }
  else show(text, undo ? undoMs : 2500, undo)
}
export async function undoLast() {
  const entry = history.undo[history.undo.length - 1]; if (!entry) return
  history.undo = history.undo.slice(0, -1)
  if (entry.redo) history.redo = history.redo.concat([entry])
  await entry.undo()
  show(`Undone: ${entry.text}`, entry.redo ? 6000 : 2500, undefined, entry.redo ? redoLast : undefined)
}
export async function redoLast() {
  const entry = history.redo[history.redo.length - 1]; if (!entry?.redo) return
  history.redo = history.redo.slice(0, -1); history.undo = history.undo.concat([entry])
  await entry.redo()
  show(`Redone: ${entry.text}`, 6000, undoLast)
}
export function undoToast() { const undo = ui.toast?.undo; ui.toast = null; undo?.() }

// Undo an accidental track change: if the track changed partway through (not at its natural end), offer to go back to the exact spot
let trackChangePlayer = ''
export function noteTrackChange(previous: any, next: any) {
  const player = app.selectedPlayer
  const samePlayer = player === trackChangePlayer; trackChangePlayer = player
  const trackOf = (status: any) => status?.playlist_loop?.find((item: any) => String(item['playlist index']) === String(status.playlist_cur_index))
  const left = trackOf(previous), arrived = trackOf(next)
  if (!samePlayer || !left || !arrived || String(left.id) === String(arrived.id) || !['play', 'pause'].includes(previous.mode)) return
  const position = Number(previous.time ?? 0) + (previous.mode === 'play' ? (Date.now() - app.receivedAt) / 1000 : 0), duration = Number(left.duration ?? previous.duration ?? 0)
  if (position < 5 || (duration && duration - position < 8)) return
  // If the queue itself was replaced (e.g. Play now on another album), undo restores the whole old queue, not just the track
  const oldQueue: any[] = previous.playlist_loop ?? [], oldIds = oldQueue.map((item: any) => Number(item.id))
  const queued = next.playlist_loop?.find((item: any) => String(item.id) === String(left.id))
  const queueReplaced = !queued && oldIds.length > 1 && oldIds.every(id => Number.isFinite(id) && id > 0)
  toast(queueReplaced ? `Queue replaced. Undo to bring back the previous queue at “${left.title}” ${formatTime(position)}?` : `Changed track. Back to “${left.title}” at ${formatTime(position)}?`, () => {
    if (queued) command('playlist', 'index', queued['playlist index'])
    else if (queueReplaced) sendToShell({ type: 'playTracks', ids: oldIds, mode: 'load', start: oldQueue.indexOf(left) })
    else { sendToShell({ type: 'playTracks', ids: [Number(left.id)], mode: 'insert' }); window.setTimeout(() => command('playlist', 'index', '+1'), 400) } // put it back next and play it
    window.setTimeout(() => command('time', Math.floor(position)), queued ? 700 : 1500) // seek once the track has started
  }, 10000, { keep: false })
}

// The current track of the selected player, from its queue
export function currentTrack() {
  const status = app.nowPlaying
  if (!status?.playlist_loop) return null
  return status.playlist_loop.find((item: any) => String(item['playlist index']) === String(status.playlist_cur_index)) ?? null
}

// ---- Playlist builder (SPEC.md › Playlist builder) ----
export function newPlaylist(name = 'Untitled playlist', items: PlaylistItem[] = []) { playlist.open = true; playlist.collapsed = false; playlist.name = name; playlist.items = items; playlist.history = []; saveDraft() }
export function saveDraft() { sendToShell({ type: 'saveDraft', draft: playlist.open ? { name: playlist.name, items: playlist.items } : null }) }
export const inPlaylist = (trackId: number) => playlist.items.some(item => item.id === trackId)

export function addToPlaylist(items: PlaylistItem[], label: string) {
  const duplicates = items.filter(item => inPlaylist(item.id)).length
  playlist.items = playlist.items.concat(items)
  playlist.history = playlist.history.concat([items.length])
  saveDraft()
  toast(`Added ${label} to ${playlist.name}${duplicates ? ` (${duplicates} already in it)` : ''}`, undoLastAdd)
}
export function undoLastAdd() {
  const lastCount = playlist.history[playlist.history.length - 1]
  if (!lastCount) return
  playlist.items = playlist.items.slice(0, playlist.items.length - lastCount)
  playlist.history = playlist.history.slice(0, -1)
  saveDraft()
}
export async function albumItems(albumKey: string): Promise<PlaylistItem[]> {
  const albumData = await query('album', { key: albumKey })
  return (albumData.tracks ?? []).map((track: any) => ({ id: track.id, title: track.title, artist: track.artist || track.albumArtist || '', duration: track.duration }))
}

export const formatTime = (seconds: number) => { if (!seconds || seconds < 0) return '0:00'; const whole = Math.floor(seconds); const hours = Math.floor(whole / 3600); const minutes = Math.floor((whole % 3600) / 60); const secs = String(whole % 60).padStart(2, '0'); return hours ? `${hours}:${String(minutes).padStart(2, '0')}:${secs}` : `${minutes}:${secs}` }
export const formatRate = (rate: number) => rate ? `${(rate / 1000).toFixed(rate % 1000 ? 1 : 0)} kHz` : ''

// What the meters draw: eased towards the latest levels on every display frame, so needles and bars glide between the shell's ~60 updates a second
export const meters = $state({ vu: [-90, -90], peak: [0, 0], bands: Array(10).fill(0) as number[], reference: -18 })
let meterFrame = 0, meterLastTime = 0
// Auto 0 VU: follows the music's loudness (power average over ~8 s) so loud masters don't pin the needles; average loudness reads about -4 VU
let loudnessPower = 0, loudnessAt = 0
export function receiveLevels(vu: number[], peak: number[], bands: number[]) {
  const receivedAt = Date.now(), loudestDb = Math.max(vu[0] ?? -90, vu[1] ?? -90)
  if (loudestDb > -50) {
    const follow = loudnessPower ? 1 - Math.exp(-Math.min(1000, receivedAt - loudnessAt) / 8000) : 1
    loudnessPower += (Math.pow(10, loudestDb / 10) - loudnessPower) * follow
  }
  loudnessAt = receivedAt
  meters.reference = app.meterSettings.autoReference && loudnessPower ? Math.max(-24, Math.min(-6, 10 * Math.log10(loudnessPower) + 4)) : app.meterSettings.referenceDbfs
  app.levels = { vu, peak, bands, at: receivedAt }
  if (!meterFrame) { meterLastTime = performance.now(); meterFrame = requestAnimationFrame(easeMeters) }
}
function easeMeters(time: number) {
  const elapsed = time - meterLastTime, follow = 1 - Math.exp(-elapsed / 35), needleFollow = 1 - Math.exp(-elapsed / 75); meterLastTime = time // needles glide over the uneven arrival of updates (the VU is 300 ms-integrated anyway)
  const ease = (shown: number[], target: number[]) => target.map((value, index) => (shown[index] ?? value) + (value - (shown[index] ?? value)) * follow)
  const vuTarget = Date.now() - app.levels.at > 1000 ? [-90, -90] : app.levels.vu
  meters.vu = vuTarget.map((value, index) => (meters.vu[index] ?? value) + (value - (meters.vu[index] ?? value)) * needleFollow)
  meters.peak = ease(meters.peak, Date.now() - app.levels.at > 1000 ? [0, 0] : app.levels.peak)
  meters.bands = ease(meters.bands, Date.now() - app.levels.at > 1000 ? meters.bands.map(() => 0) : app.levels.bands)
  const resting = Date.now() - app.levels.at > 1000 && meters.vu.every(value => value < -80) && meters.peak.every(value => value < 0.001) && meters.bands.every(value => value < 0.005)
  meterFrame = resting ? 0 : requestAnimationFrame(easeMeters)
}
