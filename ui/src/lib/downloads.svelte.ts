// Offline downloads (SPEC.md › Offline downloads): the shell downloads and keeps the list; this mirrors it for the Downloaded section and the cover badges
import { request, sendToShell, onShellMessage } from '../bridge'
import { toast } from '../state.svelte'
import { t, tn } from './i18n'
import { resolveTracks } from './browseActions'

export type DownloadTrack = { id: number; title: string; state: string; error: string; received: number; size: number }
export type DownloadGroup = { key: string; title: string; artist: string; cover: string; total: number; done: number; failed: number; downloading: number; received: number; size: number; tracks: DownloadTrack[] }
export const downloads = $state({ active: [] as DownloadGroup[], albums: {} as Record<string, 'full' | 'partial'>, finishedTracks: 0, totalBytes: 0, waiting: false, problem: '', folder: '' })
onShellMessage('downloads', message => { downloads.active = message.active ?? []; downloads.albums = message.albums ?? {}; downloads.finishedTracks = message.finishedTracks ?? 0; downloads.totalBytes = message.totalBytes ?? 0; downloads.waiting = !!message.waiting; downloads.problem = message.problem ?? ''; downloads.folder = message.folder ?? '' })

// Saved playlists aren't in the index, so their tracks are looked up first
const itemsFor = async (kind: string, keys: string[]) => kind === 'playlist' ? (await resolveTracks(kind, keys)).map(track => ({ kind: 'track', key: String(track.id) })) : keys.map(key => ({ kind, key }))

export async function downloadItems(kind: string, keys: string[]) {
  const result = await request({ type: 'download', items: await itemsFor(kind, keys) })
  if (result?.error) { toast(t('Couldn’t download: {error}', { error: result.error })); return }
  const added = Number(result?.added ?? 0)
  toast(added ? tn(added, 'Downloading {count} track (see Downloaded)', 'Downloading {count} tracks (see Downloaded)') : t('Already downloaded'))
}
export async function removeDownloads(kind: string, keys: string[]) {
  const result = await request({ type: 'downloadRemove', items: keys.map(key => ({ kind, key })) })
  const removed = Number(result?.removed ?? 0)
  toast(tn(removed, 'Removed {count} downloaded track from this PC', 'Removed {count} downloaded tracks from this PC'))
}
export const cancelDownloads = (albums: string[], tracks: number[] = []) => sendToShell({ type: 'downloadCancel', albums, tracks })
export const formatBytes = (bytes: number) => bytes >= 1e9 ? `${(bytes / 1e9).toFixed(1)} GB` : bytes >= 1e6 ? `${Math.round(bytes / 1e6)} MB` : `${Math.max(0, Math.round(bytes / 1e3))} KB`

// Copy to folder (SPEC.md › Copy to folder): the shell asks for a folder and copies in the background; progress shows in the action strip
export async function copyToFolder(kind: string, keys: string[], mp3: boolean) {
  const ids = (await resolveTracks(kind, keys)).map(track => track.id)
  if (!ids.length) { toast(t('Nothing to copy there')); return }
  const result = await request({ type: 'copyToFolder', ids, mp3 })
  if (result?.error) toast(t('Couldn’t copy: {error}', { error: result.error }))
}
onShellMessage('copyProgress', message => {
  const folder = String(message.folder ?? ''), openFolder = { action: { label: t('Open folder'), key: 'O', run: () => sendToShell({ type: 'openFolder', path: folder }) } }
  if (!message.finished) { toast(t('Copying {done} of {total} to {folder}…', { done: message.done + message.failed + 1, total: message.total, folder }), undefined, 600000, openFolder); return }
  if (message.failed) toast(t('Copied {done} of {total} to {folder}. {failed} couldn’t be copied: {problem}', { done: message.done, total: message.total, folder, failed: message.failed, problem: message.problem }), undefined, 20000, openFolder)
  else toast(tn(message.done, 'Copied {count} track to {folder}', 'Copied {count} tracks to {folder}', { folder }), undefined, 15000, openFolder)
})
