// The action strip's actions for anything you can browse: resolved to tracks by the shell's index, then played,
// queued or added to a playlist, each with Undo
import { query, request, live, sendToShell } from '../bridge'
import { app, toast, playlist, newPlaylist, addToPlaylist, type PlaylistItem } from '../state.svelte'
import type { SelectionAction, MenuChoice } from './selection.svelte'

export const resolveTracks = async (kind: string, keys: string[]): Promise<PlaylistItem[]> => {
  if (kind === 'playlist') { const lists = await Promise.all(keys.map(key => live(['playlists', 'tracks', 0, 2000, `playlist_id:${key}`, 'tags:a']))); return lists.flatMap(list => (list?.playlisttracks_loop ?? []).map((item: any) => ({ id: Number(item.id), title: item.title, artist: item.artist ?? '', duration: Number(item.duration || 0) }))) }
  const result = await query('trackItems', { items: keys.map(key => ({ kind, key })) })
  return Array.isArray(result) ? result.map((track: any) => ({ id: Number(track.id), title: track.title, artist: track.artist ?? '', duration: Number(track.duration || 0) })) : []
}
const describe = (items: PlaylistItem[]) => items.length === 1 ? `“${items[0].title}”` : `${items.length} tracks`

// Queue snapshot for undoing a Play now (which replaces the queue)
const queueSnapshot = () => ({ ids: (app.nowPlaying?.playlist_loop ?? []).map((item: any) => Number(item.id)).filter((id: number) => id > 0), index: Number(app.nowPlaying?.playlist_cur_index ?? 0), time: Number(app.nowPlaying?.time ?? 0) })

export async function queueTracks(items: PlaylistItem[], mode: 'load' | 'insert' | 'add') {
  if (!items.length) { toast('Nothing to play there'); return }
  const before = queueSnapshot(), queueLength = Number(app.nowPlaying?.playlist_tracks ?? before.ids.length)
  sendToShell({ type: 'playTracks', ids: items.map(item => item.id), mode, start: 0 })
  const label = mode === 'load' ? `Playing ${describe(items)}` : mode === 'insert' ? `${describe(items)} play next` : `Added ${describe(items)} to Now playing`
  toast(label, async () => {
    if (mode === 'load') { if (!before.ids.length) { await live(['playlist', 'clear'], true); return } await live(['playlistcontrol', 'cmd:load', `track_id:${before.ids.join(',')}`], true); await live(['playlist', 'index', before.index], true); await live(['time', Math.floor(before.time)], true); return }
    const firstAdded = mode === 'insert' ? before.index + 1 : queueLength
    for (let position = firstAdded + items.length - 1; position >= firstAdded; position--) await live(['playlist', 'delete', position], true)
  })
}

async function playlistMenu(itemsFor: () => Promise<PlaylistItem[]>): Promise<MenuChoice[]> {
  const saved = await live(['playlists', 0, 500])
  const choices: MenuChoice[] = []
  if (playlist.open) choices.push({ label: playlist.name, detail: 'being built', run: async () => { const items = await itemsFor(); addToPlaylist(items, describe(items)) } })
  choices.push({ label: 'New playlist…', detail: 'start building one with these', run: async () => newPlaylist('Untitled playlist', await itemsFor()) })
  for (const savedList of saved?.playlists_loop ?? []) choices.push({ label: savedList.playlist, run: async () => {
    const items = await itemsFor(), result = await request({ type: 'addToSavedPlaylist', playlistId: savedList.id, ids: items.map(item => item.id) })
    toast(`Added ${describe(items)} to ${savedList.playlist}`, async () => { for (let index = Number(result?.before ?? 0) + items.length - 1; index >= Number(result?.before ?? 0); index--) await live(['playlists', 'edit', 'cmd:delete', `playlist_id:${savedList.id}`, `index:${index}`]) })
  } })
  return choices
}

export function browseActions(kind: string): SelectionAction[] {
  return [
    { label: 'Play now', key: 'Enter', run: async keys => queueTracks(await resolveTracks(kind, keys), 'load') },
    { label: 'Play next', run: async keys => queueTracks(await resolveTracks(kind, keys), 'insert') },
    { label: 'Add to end', run: async keys => queueTracks(await resolveTracks(kind, keys), 'add') },
    { label: 'Add to playlist…', menu: async keys => playlistMenu(() => resolveTracks(kind, keys)) }
  ]
}
