import { sendToShell, query, command } from './bridge'
import { t } from './lib/i18n'

export type View = { kind: string; key?: string; title?: string; highlight?: string; filters?: CustomStep[]; scroll?: number }
// A step taken inside a custom view (SPEC.md › Custom views): the row chosen at that level
export type CustomStep = { kind: string; key: string; name: string }
export type PlaylistItem = { id: number; title: string; artist: string; duration: number }

export const app = $state({
  status: null as any, nowPlaying: null as any, players: [] as any[], syncGroups: [] as string[][], groupVolumes: {} as Record<string, number>, selectedPlayer: '', ownPlayer: '',
  library: { text: '', done: 0, total: 0 }, librarySettings: { AlbumSource: 'folders', ContributorGrouping: 'any' } as any, playback: { QueueEnd: 'stop', ContinueAdds: 'album', ContinueFrom: 'artist' } as Record<string, string>, hints: { hidden: false, dismissed: [] as string[] }, trackInfoTabs: [] as string[], indexVersion: 0, receivedAt: 0,
  // Meter data from Squeezelite (~30 a second while playing): VU in dBFS, peak amplitude 0..1, spectrum bands 0..1
  levels: { vu: [-90, -90], peak: [0, 0], bands: Array(10).fill(0), wide: [] as number[], at: 0 }, // wide: Transporter's spectrum, 53 bands a channel (left then right), sent only while it's up
  meterSettings: { referenceDbfs: -18, autoReference: true, styles: ['vu', 'green', 'oled'] as string[] }, // Settings › Meters (styles: the ones a click cycles through, in order)
  window: { glass: false, frameless: false, maximised: false }
})
// Where you were browsing survives restarts (and updates)
const savedNav = (() => { try { return JSON.parse(readSetting('nav', '')) } catch { return null } })()
export const nav = $state({ section: (savedNav?.section ?? 'artists') as string, stack: (savedNav?.stack ?? []) as View[] })
$effect.root(() => { $effect(() => { writeSetting('nav', JSON.stringify({ section: nav.section, stack: nav.stack })) }) })
export const ui = $state({ search: '', scope: 'list' as 'list' | 'everything', expanded: false, meterStyle: readSetting('meterStyle', 'vu').replace(/^ledbar$/, 'vu'), skin: readSetting('skin', 'auto'), coverAccent: readSetting('coverAccent', '') === '1', coverBackdrop: readSetting('coverBackdrop', '1') !== '0', accent: readSetting('accent', ''), glassTransparency: Number(readSetting('glassTransparency', '-1')), glassTintHue: Number(readSetting('glassTintHue', '0')), lookVersion: 0, infoTab: readSetting('infoTab', 'queue'), pinned: false, metersFull: false, hifi: false, transporter: false, trackInfo: null as null | number[], itemInfo: null as null | { name: string, fields: [string, string][] }[], toast: null as null | { text: string; undo?: () => unknown; redo?: () => unknown; action?: { label: string; key: string; run: () => unknown }; ms: number; id: number } })
export const playlist = $state({ open: false, collapsed: false, name: t('Untitled playlist'), items: [] as PlaylistItem[], history: [] as number[] })

export function readSetting(name: string, fallback: string) { try { return localStorage.getItem('ws.' + name) ?? fallback } catch { return fallback } }
export function writeSetting(name: string, value: string) { try { localStorage.setItem('ws.' + name, value) } catch { } }

// Forward history, like a browser: Back keeps what it left, any new navigation clears it
export const forwardHistory = $state({ views: [] as View[] })
// A sidebar click starts its section afresh from the top; the cover art on the transport bar flips between Now playing and the view before it, which comes back at the same level and scroll (restore)
type SectionMemory = { stack: View[]; scroll: number; forward: View[] }
const sectionMemory: Record<string, SectionMemory> = {}
export function openSection(section: string, restore = false) {
  rememberScroll(); sectionMemory[nav.section] = { stack: nav.stack, scroll: sectionScroll, forward: forwardHistory.views }
  const saved = restore ? sectionMemory[section] : undefined
  nav.section = section; nav.stack = saved?.stack ?? []; ui.search = ''; forwardHistory.views = saved?.forward ?? []; sectionScroll = saved?.scroll ?? 0
  restoreScroll(nav.stack.length ? nav.stack[nav.stack.length - 1].scroll ?? 0 : sectionScroll)
}
// Scroll positions, like a browser: each view keeps how far down it was, Back and Forward return there, new views start at the top
let sectionScroll = 0, restoreRun = 0
const scrollHost = () => document.querySelector<HTMLElement>('.body > main.content:not(.now-playing-host)')
function rememberScroll() {
  if (ui.search.length >= 2 && ui.scope === 'everything') return // search results fill the pane, not this view
  const top = scrollHost()?.scrollTop ?? 0, leaving = nav.stack[nav.stack.length - 1]
  if (leaving) leaving.scroll = top; else sectionScroll = top
}
// Lists fill in once their data arrives, so keep putting the position back until the view is tall enough; the person scrolling or a 4 s limit ends it
function restoreScroll(top: number) {
  const run = ++restoreRun, started = performance.now()
  const cancel = () => { if (run === restoreRun) restoreRun++ }, events = ['wheel', 'pointerdown', 'keydown', 'touchstart']
  const finish = () => { for (const name of events) removeEventListener(name, cancel, true) }
  if (top > 0) for (const name of events) addEventListener(name, cancel, true)
  const step = () => {
    if (run !== restoreRun) return finish()
    const host = scrollHost(); if (host) host.scrollTop = top
    if ((host && Math.abs(host.scrollTop - top) < 2) || performance.now() - started > 4000) return finish()
    requestAnimationFrame(step)
  }
  requestAnimationFrame(step)
}
// A person named in tag text ("Kapustin, Nikolai; Bach, J.S."): the shell splits it the way the lists do and gives the first person's key
export async function personView(kind: string, name: string): Promise<View> { const person = await query('personKey', { name, kind }); return { kind, key: person?.key ?? name.toLowerCase(), title: person?.name ?? name } }
export function openView(view: View) { rememberScroll(); nav.stack = nav.stack.concat([view]); ui.search = ''; forwardHistory.views = []; restoreScroll(0) }
export function back() { if (!nav.stack.length) return; rememberScroll(); forwardHistory.views = forwardHistory.views.concat([nav.stack[nav.stack.length - 1]]); nav.stack = nav.stack.slice(0, -1); restoreScroll(nav.stack[nav.stack.length - 1]?.scroll ?? sectionScroll) }
export function forward() { if (!forwardHistory.views.length) return; rememberScroll(); const next = forwardHistory.views[forwardHistory.views.length - 1]; nav.stack = nav.stack.concat([next]); forwardHistory.views = forwardHistory.views.slice(0, -1); restoreScroll(next.scroll ?? 0) }
export const currentView = (): View => nav.stack.length ? nav.stack[nav.stack.length - 1] : pins.views[nav.section] ?? sectionView(nav.section)
// Custom views sit in the sidebar as "custom:<id>", or several under one "customgroup:<group name>"
const sectionView = (section: string): View => section.startsWith('custom:') ? { kind: 'custom', key: section.slice(7), filters: [] } : section.startsWith('customgroup:') ? { kind: 'customGroup', key: section.slice(12) } : { kind: section }
// Sidebar shortcuts: any genre, artist, album, folder or playlist pinned as its own sidebar section ("pin:<kind>:<key>"), remembered per PC
export const pins = $state({ views: (() => { try { return JSON.parse(readSetting('pins', '{}')) } catch { return {} } })() as Record<string, View> })
export const pinKey = (view: View) => `pin:${view.kind}:${view.key ?? ''}`
export function togglePin(view: View) {
  const key = pinKey(view), next: Record<string, View> = {}
  for (const [existing, pinned] of Object.entries(pins.views)) if (existing !== key) next[existing] = pinned
  if (!pins.views[key]) next[key] = { kind: view.kind, key: view.key, title: view.title }
  pins.views = next; writeSetting('pins', JSON.stringify(next))
  toast(pins.views[key] ? t('Pinned “{name}” to the sidebar', { name: view.title }) : t('Unpinned “{name}”', { name: view.title }), () => togglePin(view), 6000, { keep: false })
}

// Messages show in the action strip above the transport (ActionStrip.svelte), with a ring counting down their time
let toastTimer = 0, toastId = 0
// Undo history: every undoable action stays undoable (Ctrl+Z) after its bar has gone; actions that pass a redo can be redone (Ctrl+Y / Ctrl+Shift+Z)
export type ToastAction = { label: string; key: string; run: () => unknown } // an extra button on the bar, with a one-letter shortcut
type HistoryEntry = { text: string; undo: () => unknown; redo?: () => unknown }
const historyLimit = 100
export const history = $state({ undo: [] as HistoryEntry[], redo: [] as HistoryEntry[] })
function show(text: string, ms: number, undo?: () => unknown, redo?: () => unknown, action?: ToastAction) { ui.toast = { text, undo, redo, action, ms, id: ++toastId }; clearTimeout(toastTimer); toastTimer = window.setTimeout(() => ui.toast = null, ms) }
// keep: false for undos that only make sense straight away (going back to a track you just left)
export function toast(text: string, undo?: () => unknown, undoMs = 6000, options: { redo?: () => unknown; keep?: boolean; action?: ToastAction } = {}) {
  if (options.action && !undo) { show(text, undoMs, undefined, undefined, options.action); return }
  if (undo && options.keep !== false) { history.undo = history.undo.concat([{ text, undo, redo: options.redo }]).slice(-historyLimit); history.redo = []; show(text, undoMs, undoLast) }
  else show(text, undo ? undoMs : 2500, undo)
}
export async function undoLast() {
  const entry = history.undo[history.undo.length - 1]; if (!entry) return
  history.undo = history.undo.slice(0, -1)
  if (entry.redo) history.redo = history.redo.concat([entry])
  await entry.undo()
  show(t('Undone: {action}', { action: entry.text }), entry.redo ? 6000 : 2500, undefined, entry.redo ? redoLast : undefined)
}
export async function redoLast() {
  const entry = history.redo[history.redo.length - 1]; if (!entry?.redo) return
  history.redo = history.redo.slice(0, -1); history.undo = history.undo.concat([entry])
  await entry.redo()
  show(t('Redone: {action}', { action: entry.text }), 6000, undoLast)
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
  toast(queueReplaced ? t('Queue replaced. Undo to bring back the previous queue at “{title}” {time}?', { title: left.title, time: formatTime(position) }) : t('Changed track. Back to “{title}” at {time}?', { title: left.title, time: formatTime(position) }), () => {
    if (queued) command('playlist', 'index', queued['playlist index'])
    else if (queueReplaced) sendToShell({ type: 'playTracks', ids: oldIds, mode: 'load', start: oldQueue.indexOf(left) })
    else { sendToShell({ type: 'playTracks', ids: [Number(left.id)], mode: 'insert' }); window.setTimeout(() => command('playlist', 'index', '+1'), 400) } // put it back next and play it
    window.setTimeout(() => command('time', Math.floor(position)), queued ? 700 : 1500) // seek once the track has started
  }, 10000, { keep: false })
}

// Right-click on the meters cycles: Now playing -> full-window meters -> hi-fi separate -> Transporter -> back; the shell reshapes the window for the two units
export function setHifi(on: boolean) { ui.hifi = on; if (on) { ui.metersFull = false; ui.transporter = false } sendToShell({ type: 'hifiWindow', on }) }
export function setTransporter(on: boolean) { ui.transporter = on; if (on) { ui.metersFull = false; ui.hifi = false } sendToShell({ type: 'hifiWindow', on, shape: 'transporter' }); sendToShell({ type: 'wideSpectrum', on }) }
export function cycleMeterView() { if (ui.transporter) setTransporter(false); else if (ui.hifi) setTransporter(true); else if (ui.metersFull) setHifi(true); else ui.metersFull = true }

// The current track of the selected player, from its queue
export function currentTrack() {
  const status = app.nowPlaying
  if (!status?.playlist_loop) return null
  return status.playlist_loop.find((item: any) => String(item['playlist index']) === String(status.playlist_cur_index)) ?? null
}

// ---- Playlist builder (SPEC.md › Playlist builder) ----
export function newPlaylist(name = t('Untitled playlist'), items: PlaylistItem[] = []) { playlist.open = true; playlist.collapsed = false; playlist.name = name; playlist.items = items; playlist.history = []; saveDraft() }
export function saveDraft() { sendToShell({ type: 'saveDraft', draft: playlist.open ? { name: playlist.name, items: playlist.items } : null }) }
export const inPlaylist = (trackId: number) => playlist.items.some(item => item.id === trackId)

export function addToPlaylist(items: PlaylistItem[], label: string) {
  const duplicates = items.filter(item => inPlaylist(item.id)).length
  playlist.items = playlist.items.concat(items)
  playlist.history = playlist.history.concat([items.length])
  saveDraft()
  toast(t('Added {items} to {playlist}', { items: label, playlist: playlist.name }) + (duplicates ? ' ' + t('({count} already in it)', { count: duplicates }) : ''), undoLastAdd)
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
export const meters = $state({ vu: [-90, -90], peak: [0, 0], bands: Array(10).fill(0) as number[], wide: [] as number[], reference: -18, jog: [0, 0], lamp: [0, 0] })
let meterFrame = 0, meterLastTime = 0
// Jog: dragging the window shakes the VU needles (degrees added to their angle). Each movement is a lightly damped spring, the two a little out of tune so they drift apart
const jogSpeed = [0, 0], jogTuning = [13.5, 15.5], jogDamping = 0.22
let windowX = NaN, windowMovedAt = 0, windowSpeed = 0
export function windowMoved(x: number) {
  const movedAt = performance.now(), seconds = Math.max(0.008, (movedAt - windowMovedAt) / 1000)
  const speed = Number.isFinite(windowX) && seconds < 0.25 ? Math.max(-4000, Math.min(4000, (x - windowX) / seconds)) : 0 // pixels a second; snaps and long gaps are capped
  kickNeedles(speed - windowSpeed); windowX = x; windowMovedAt = movedAt; windowSpeed = speed
  startMeterFrames()
}
// The case speeds up one way, the needles lag the other way (~10° for a brisk 1000 px/s drag)
const kickNeedles = (speedChange: number) => { for (let channel = 0; channel < 2; channel++) jogSpeed[channel] -= speedChange * 0.14 }
// A tap on the meter: both needles kick up and wobble back, a little differently
export function tapMeters() { jogSpeed[0] += 70; jogSpeed[1] += 58; startMeterFrames() }
function swingNeedles(elapsed: number) {
  if (windowSpeed && performance.now() - windowMovedAt > 60) { kickNeedles(-windowSpeed); windowSpeed = 0 } // the window stopped: the needles swing on
  const steps = Math.max(1, Math.ceil(elapsed / 4)), seconds = elapsed / 1000 / steps
  meters.jog = meters.jog.map((angle, channel) => {
    for (let step = 0; step < steps; step++) { jogSpeed[channel] += (-jogTuning[channel] * jogTuning[channel] * angle - 2 * jogDamping * jogTuning[channel] * jogSpeed[channel]) * seconds; angle += jogSpeed[channel] * seconds }
    const settled = Math.abs(angle) < 0.05 && Math.abs(jogSpeed[channel]) < 0.5
    if (settled || !Number.isFinite(angle)) jogSpeed[channel] = 0
    return settled || !Number.isFinite(angle) ? 0 : Math.max(-25, Math.min(25, angle))
  })
}
// Peak lamp (the beige VU's red lens): a small bulb lit by peaks past 1-3 dB under full scale, brighter the further past (over 3 dB). The filament heats in ~30 ms and cools over ~300 ms, so a short peak only glows dimly
function heatPeakLamps(elapsed: number) {
  const live = Date.now() - app.levels.at <= 1000, threshold = Math.min(meters.reference + 15, -1)
  meters.lamp = meters.lamp.map((heat, channel) => {
    const peak = live ? app.levels.peak[channel] ?? 0 : 0, drive = peak > 0 ? Math.max(0, Math.min(1, (20 * Math.log10(peak) - threshold) / 3)) : 0
    const next = heat + (drive - heat) * (1 - Math.exp(-elapsed / (drive > heat ? 30 : 300)))
    return Number.isFinite(next) && next > 0.003 ? next : 0
  })
}
function startMeterFrames() { if (!meterFrame) { meterLastTime = performance.now(); meterFrame = requestAnimationFrame(easeMeters) } }
// Auto 0 VU: follows the music's loudness (power average over ~8 s) so loud masters don't pin the needles; average loudness reads about -4 VU
let loudnessPower = 0, loudnessAt = 0
export function receiveLevels(vu: number[], peak: number[], bands: number[], wide: number[] | null = null) {
  const receivedAt = Date.now(), loudestDb = Math.max(vu[0] ?? -90, vu[1] ?? -90)
  if (loudestDb > -50) {
    const follow = loudnessPower > 0 ? 1 - Math.exp(-Math.max(0, Math.min(1000, receivedAt - loudnessAt)) / 8000) : 1 // clamped: the clock can jump back after sleep
    loudnessPower += (Math.pow(10, loudestDb / 10) - loudnessPower) * follow
    if (!Number.isFinite(loudnessPower) || loudnessPower < 0) loudnessPower = 0
  }
  loudnessAt = receivedAt
  // in half-dB steps: the VU face's percent scale follows it, and redrawing that face on every update stalled the needles
  meters.reference = app.meterSettings.autoReference && loudnessPower ? Math.round(2 * Math.max(-24, Math.min(-6, 10 * Math.log10(loudnessPower) + 4))) / 2 : app.meterSettings.referenceDbfs
  app.levels = { vu, peak, bands, wide: wide ?? [], at: receivedAt }
  startMeterFrames()
}
function easeMeters(time: number) {
  const elapsed = Math.max(0, Math.min(250, time - meterLastTime)), follow = 1 - Math.exp(-elapsed / 35), needleFollow = 1 - Math.exp(-elapsed / 25); meterLastTime = time // needles glide over the uneven arrival of updates without adding lag (the shell already applies VU ballistics)
  const from = (shown: number[], index: number, value: number) => Number.isFinite(shown[index]) ? shown[index] : value // a bad value never sticks
  const ease = (shown: number[], target: number[]) => target.map((value, index) => from(shown, index, value) + (value - from(shown, index, value)) * follow)
  const vuTarget = Date.now() - app.levels.at > 1000 ? [-90, -90] : app.levels.vu
  meters.vu = vuTarget.map((value, index) => from(meters.vu, index, value) + (value - from(meters.vu, index, value)) * needleFollow)
  meters.peak = ease(meters.peak, Date.now() - app.levels.at > 1000 ? [0, 0] : app.levels.peak)
  meters.bands = ease(meters.bands, Date.now() - app.levels.at > 1000 ? meters.bands.map(() => 0) : app.levels.bands)
  meters.wide = ease(meters.wide, Date.now() - app.levels.at > 1000 ? meters.wide.map(() => 0) : app.levels.wide)
  heatPeakLamps(elapsed)
  if (windowSpeed || meters.jog[0] || meters.jog[1] || jogSpeed[0] || jogSpeed[1]) swingNeedles(elapsed)
  const resting = !meters.jog[0] && !meters.jog[1] && !windowSpeed && Date.now() - app.levels.at > 1000 && meters.vu.every(value => value < -80) && meters.peak.every(value => value < 0.001) && meters.bands.every(value => value < 0.005) && meters.wide.every(value => value < 0.005) && meters.lamp.every(heat => !heat)
  meterFrame = resting ? 0 : requestAnimationFrame(easeMeters)
}
// Back from the tray or a minimised window: restart the frame loop and redraw the meters from scratch (meterEpoch remounts them)
export const meterView = $state({ epoch: 0 })
document.addEventListener('visibilitychange', () => {
  if (document.hidden) return
  cancelAnimationFrame(meterFrame); meterLastTime = performance.now(); meterFrame = requestAnimationFrame(easeMeters); meterView.epoch++
})
