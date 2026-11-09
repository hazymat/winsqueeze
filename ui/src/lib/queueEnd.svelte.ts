import { command, live, request, sendToShell } from '../bridge'
import { app, toast } from '../state.svelte'
import { t } from './i18n'
// When the queue ends: stop, repeat all (Lyrion's own repeat), or keep playing (we top the queue up from the library just before the last track ends)
export type QueueEnd = 'stop' | 'repeat' | 'continue'
export const queueEndMode = (): QueueEnd => Number(app.nowPlaying?.['playlist repeat']) === 2 ? 'repeat' : app.playback.QueueEnd === 'continue' ? 'continue' : 'stop'
export function setPlayback(changes: Record<string, string>) {
  for (const [name, value] of Object.entries(changes)) app.playback[name] = value
  sendToShell({ type: 'setPlayback', queueEnd: changes.QueueEnd, continueAdds: changes.ContinueAdds, continueFrom: changes.ContinueFrom })
}
export function setQueueEnd(mode: QueueEnd) { setPlayback({ QueueEnd: mode }); command('playlist', 'repeat', mode === 'repeat' ? 2 : 0) }

let toppedFor = '' // once per last track, so a slow add or an undo doesn't trigger another top-up
export async function checkKeepPlaying(status: any) {
  if (app.playback.QueueEnd !== 'continue' || status?.mode !== 'play' || Number(status?.['playlist repeat']) || Number(status?.remote) === 1) return
  const total = Number(status.playlist_tracks ?? 0), index = Number(status.playlist_cur_index ?? -1)
  if (!total || index !== total - 1) return
  const track = status.playlist_loop?.find((item: any) => String(item['playlist index']) === String(index))
  const secondsLeft = Number(status.duration ?? track?.duration ?? 0) - Number(status.time ?? 0)
  if (!(Number(track?.id) > 0) || secondsLeft > 20) return
  const marker = `${app.selectedPlayer}:${index}:${track.id}`; if (marker === toppedFor) return; toppedFor = marker
  const queued = (status.playlist_loop ?? []).map((item: any) => Number(item.id)).filter((id: number) => id > 0)
  const pick = await request<{ ids: number[]; label: string }>({ type: 'continuePick', track: track.id, queued })
  if (!pick?.ids?.length) return
  await live(['playlistcontrol', 'cmd:add', `track_id:${pick.ids.join(',')}`], true)
  toast(t('Keep playing added {items}', { items: pick.label }), async () => { for (let position = total + pick.ids.length - 1; position >= total; position--) await live(['playlist', 'delete', position], true); toast(t('Removed what Keep playing added')) }, 10000)
}
