import { sendToShell, query } from './bridge'

export type View = { kind: string; key?: string; title?: string }
export type PlaylistItem = { id: number; title: string; artist: string; duration: number }

export const app = $state({
  status: null as any, nowPlaying: null as any, players: [] as any[], selectedPlayer: '', ownPlayer: '',
  library: { text: '', done: 0, total: 0 }, librarySettings: { AlbumSource: 'folders', ContributorGrouping: 'any' } as any, indexVersion: 0, receivedAt: 0,
  // Meter data from Squeezelite (~30 a second while playing): VU in dBFS, peak amplitude 0..1, spectrum bands 0..1
  levels: { vu: [-90, -90], peak: [0, 0], bands: [0, 0, 0, 0, 0, 0, 0, 0], at: 0 },
  meterSettings: { referenceDbfs: -18, spectrumFloorDbfs: -54, spectrumCeilingDbfs: -6 }, // Settings › Meters
  window: { glass: false, frameless: false, maximised: false }
})
// Where you were browsing survives restarts (and updates)
const savedNav = (() => { try { return JSON.parse(readSetting('nav', '')) } catch { return null } })()
export const nav = $state({ section: (savedNav?.section ?? 'artists') as string, stack: (savedNav?.stack ?? []) as View[] })
$effect.root(() => { $effect(() => { writeSetting('nav', JSON.stringify({ section: nav.section, stack: nav.stack })) }) })
export const ui = $state({ search: '', scope: 'list' as 'list' | 'everything', expanded: false, meterStyle: readSetting('meterStyle', 'vu'), skin: readSetting('skin', 'auto'), coverAccent: readSetting('coverAccent', '') === '1', accent: readSetting('accent', ''), glassTransparency: Number(readSetting('glassTransparency', '-1')), glassTintHue: Number(readSetting('glassTintHue', '0')), pinned: false, toast: null as null | { text: string; undo?: () => void; ms: number; id: number } })
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
export function toast(text: string, undo?: () => void) { const ms = undo ? 6000 : 2500; ui.toast = { text, undo, ms, id: ++toastId }; clearTimeout(toastTimer); toastTimer = window.setTimeout(() => ui.toast = null, ms) }
export function undoToast() { const undo = ui.toast?.undo; ui.toast = null; undo?.() }

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
export const meters = $state({ vu: [-90, -90], peak: [0, 0], bands: [0, 0, 0, 0, 0, 0, 0, 0] })
let meterFrame = 0, meterLastTime = 0
export function receiveLevels(vu: number[], peak: number[], bands: number[]) {
  app.levels = { vu, peak, bands, at: Date.now() }
  if (!meterFrame) { meterLastTime = performance.now(); meterFrame = requestAnimationFrame(easeMeters) }
}
function easeMeters(time: number) {
  const follow = 1 - Math.exp(-(time - meterLastTime) / 35); meterLastTime = time
  const ease = (shown: number[], target: number[]) => target.map((value, index) => (shown[index] ?? value) + (value - (shown[index] ?? value)) * follow)
  meters.vu = ease(meters.vu, Date.now() - app.levels.at > 1000 ? [-90, -90] : app.levels.vu)
  meters.peak = ease(meters.peak, Date.now() - app.levels.at > 1000 ? [0, 0] : app.levels.peak)
  meters.bands = ease(meters.bands, Date.now() - app.levels.at > 1000 ? meters.bands.map(() => 0) : app.levels.bands)
  const resting = Date.now() - app.levels.at > 1000 && meters.vu.every(value => value < -80) && meters.peak.every(value => value < 0.001) && meters.bands.every(value => value < 0.005)
  meterFrame = resting ? 0 : requestAnimationFrame(easeMeters)
}
