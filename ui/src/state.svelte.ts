import { sendToShell, query } from './bridge'

export type View = { kind: string; key?: string; title?: string }
export type PlaylistItem = { id: number; title: string; artist: string; duration: number }

export const app = $state({
  status: null as any, nowPlaying: null as any, players: [] as any[], selectedPlayer: '', ownPlayer: '',
  library: { text: '', done: 0, total: 0 }, librarySettings: { AlbumSource: 'folders', ContributorGrouping: 'any' } as any, indexVersion: 0, receivedAt: 0
})
export const nav = $state({ section: 'artists', stack: [] as View[] })
export const ui = $state({ search: '', scope: 'list' as 'list' | 'everything', expanded: false, meterStyle: readSetting('meterStyle', 'vu'), skin: readSetting('skin', 'auto'), pinned: false, toast: null as null | { text: string; undo?: () => void } })
export const playlist = $state({ open: false, collapsed: false, name: 'Untitled playlist', items: [] as PlaylistItem[], history: [] as number[] })

export function readSetting(name: string, fallback: string) { try { return localStorage.getItem('ws.' + name) ?? fallback } catch { return fallback } }
export function writeSetting(name: string, value: string) { try { localStorage.setItem('ws.' + name, value) } catch { } }

export function openSection(section: string) { nav.section = section; nav.stack = []; ui.search = ''; ui.expanded = false }
export function openView(view: View) { nav.stack = nav.stack.concat([view]); ui.search = '' }
export function back() { nav.stack = nav.stack.slice(0, -1) }
export const currentView = () => nav.stack.length ? nav.stack[nav.stack.length - 1] : { kind: nav.section }

let toastTimer = 0
export function toast(text: string, undo?: () => void) { ui.toast = { text, undo }; clearTimeout(toastTimer); toastTimer = window.setTimeout(() => ui.toast = null, 4000) }

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
