// The action strip's actions for anything you can browse: resolved to tracks by the shell's index, then played,
// queued or added to a playlist, each with Undo
import { query, request, live, sendToShell } from '../bridge'
import { app, ui, toast, playlist, newPlaylist, addToPlaylist, type PlaylistItem } from '../state.svelte'
import type { SelectionAction, MenuChoice } from './selection.svelte'
import { downloadItems, copyToFolder } from './downloads.svelte'
import { t, tn } from './i18n'

export const resolveTracks = async (kind: string, keys: string[]): Promise<PlaylistItem[]> => {
  if (kind === 'playlist') { const lists = await Promise.all(keys.map(key => live(['playlists', 'tracks', 0, 2000, `playlist_id:${key}`, 'tags:a']))); return lists.flatMap(list => (list?.playlisttracks_loop ?? []).map((item: any) => ({ id: Number(item.id), title: item.title, artist: item.artist ?? '', duration: Number(item.duration || 0) }))) }
  const result = await query('trackItems', { items: keys.map(key => ({ kind, key })) })
  return Array.isArray(result) ? result.map((track: any) => ({ id: Number(track.id), title: track.title, artist: track.artist ?? '', duration: Number(track.duration || 0) })) : []
}
const describe = (items: PlaylistItem[]) => items.length === 1 ? `“${items[0].title}”` : tn(items.length, '{count} track', '{count} tracks')

// Queue snapshot for undoing a Play now (which replaces the queue)
const queueSnapshot = () => ({ ids: (app.nowPlaying?.playlist_loop ?? []).map((item: any) => Number(item.id)).filter((id: number) => id > 0), index: Number(app.nowPlaying?.playlist_cur_index ?? 0), time: Number(app.nowPlaying?.time ?? 0) })

export async function queueTracks(items: PlaylistItem[], mode: 'load' | 'insert' | 'add') {
  if (!items.length) { toast(t('Nothing to play there')); return }
  const before = queueSnapshot(), queueLength = Number(app.nowPlaying?.playlist_tracks ?? before.ids.length)
  sendToShell({ type: 'playTracks', ids: items.map(item => item.id), mode, start: 0 })
  const label = mode === 'load' ? t('Playing {items}', { items: describe(items) }) : mode === 'insert' ? t('{items} play next', { items: describe(items) }) : t('Added {items} to Now playing', { items: describe(items) })
  toast(label, async () => {
    if (mode === 'load') { if (!before.ids.length) { await live(['playlist', 'clear'], true); return } await live(['playlistcontrol', 'cmd:load', `track_id:${before.ids.join(',')}`], true); await live(['playlist', 'index', before.index], true); await live(['time', Math.floor(before.time)], true); return }
    const firstAdded = mode === 'insert' ? before.index + 1 : queueLength
    for (let position = firstAdded + items.length - 1; position >= firstAdded; position--) await live(['playlist', 'delete', position], true)
  }, 6000, { redo: () => sendToShell({ type: 'playTracks', ids: items.map(item => item.id), mode, start: 0 }) })
}

// Saved playlists: delete and overwrite keep the old tracks so Undo can put them back for a while afterwards
export const playlistUrls = async (playlistId: string): Promise<string[]> => ((await live(['playlists', 'tracks', 0, 5000, `playlist_id:${playlistId}`, 'tags:u']))?.playlisttracks_loop ?? []).map((item: any) => item.url).filter(Boolean)
export async function refillPlaylist(playlistId: string, urls: string[]) {
  const count = Number((await live(['playlists', 'tracks', 0, 1, `playlist_id:${playlistId}`]))?.count ?? 0)
  for (let index = count - 1; index >= 0; index--) await live(['playlists', 'edit', 'cmd:delete', `playlist_id:${playlistId}`, `index:${index}`])
  for (const url of urls) await live(['playlists', 'edit', 'cmd:add', `playlist_id:${playlistId}`, `url:${url}`])
}
export async function deletePlaylists(keys: string[], afterwards: () => void) {
  const saved = (await live(['playlists', 0, 1000]))?.playlists_loop ?? []
  const backups = await Promise.all(keys.map(async key => ({ name: String(saved.find((savedList: any) => String(savedList.id) === key)?.playlist ?? t('Playlist')), urls: await playlistUrls(key) })))
  for (const key of keys) await live(['playlists', 'delete', `playlist_id:${key}`])
  afterwards()
  toast(backups.length === 1 ? t('Deleted “{name}”', { name: backups[0].name }) : t('Deleted {count} playlists', { count: backups.length }), async () => {
    for (const backup of backups) { const created = await live(['playlists', 'new', `name:${backup.name}`]); if (created?.playlist_id) await refillPlaylist(String(created.playlist_id), backup.urls) }
    afterwards(); toast(backups.length === 1 ? t('Restored “{name}”', { name: backups[0].name }) : t('Playlists restored'))
  }, 30000)
}

async function playlistMenu(itemsFor: () => Promise<PlaylistItem[]>): Promise<MenuChoice[]> {
  const saved = await live(['playlists', 0, 500])
  const choices: MenuChoice[] = []
  if (playlist.open) choices.push({ label: playlist.name, detail: t('being built'), run: async () => { const items = await itemsFor(); addToPlaylist(items, describe(items)) } })
  choices.push({ label: t('New playlist…'), detail: t('start building one with these'), run: async () => newPlaylist(t('Untitled playlist'), await itemsFor()) })
  for (const savedList of saved?.playlists_loop ?? []) choices.push({ label: savedList.playlist, run: async () => {
    const items = await itemsFor(), result = await request({ type: 'addToSavedPlaylist', playlistId: savedList.id, ids: items.map(item => item.id) })
    toast(t('Added {items} to {playlist}', { items: describe(items), playlist: savedList.playlist }), async () => { for (let index = Number(result?.before ?? 0) + items.length - 1; index >= Number(result?.before ?? 0); index--) await live(['playlists', 'edit', 'cmd:delete', `playlist_id:${savedList.id}`, `index:${index}`]) }, 6000, { redo: () => request({ type: 'addToSavedPlaylist', playlistId: savedList.id, ids: items.map(item => item.id) }) })
  } })
  return choices
}

export function browseActions(kind: string): SelectionAction[] {
  return [
    { label: t('Play now'), key: 'Enter', run: async keys => queueTracks(await resolveTracks(kind, keys), 'load') },
    { label: t('Play next'), run: async keys => queueTracks(await resolveTracks(kind, keys), 'insert') },
    { label: t('Add to end'), run: async keys => queueTracks(await resolveTracks(kind, keys), 'add') },
    { label: t('Add to playlist…'), menu: async keys => playlistMenu(() => resolveTracks(kind, keys)) },
    { label: t('Download'), run: async keys => downloadItems(kind, keys) },
    // Copy to folder: for a USB stick in the car (most can't play FLAC), or the original files
    { label: t('Copy to folder…'), menu: async keys => [{ label: t('As MP3'), detail: t('plays in most cars'), run: () => copyToFolder(kind, keys, true) }, { label: t('Original files'), detail: t('same format as on the server'), run: () => copyToFolder(kind, keys, false) }] },
    { label: t('Track info'), run: async keys => { ui.trackInfo = (await resolveTracks(kind, keys)).map(item => item.id) } }
  ]
}
