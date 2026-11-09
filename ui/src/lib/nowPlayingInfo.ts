import { query } from '../bridge'
// Now playing views (SPEC.md › Now playing views): one lookup per kind for the playing track; the shell keeps answers for the session, so asking early makes the tabs instant
export const infoQuery = (kind: string, playing: any) => query('nowPlayingInfo', { kind, id: playing.id ?? 0, artistId: String(playing.artist_id ?? ''), albumId: String(playing.album_id ?? ''), artist: playing.artist ?? '', album: playing.album ?? '', title: playing.title ?? '', duration: Number(playing.duration ?? 0), remote: Number(playing.remote) === 1 ? 1 : 0 })
// The kinds the ticked tabs need: each tab, plus Timeline with Artist and Credits with Album
export const infoKinds = (tabs: string[]) => tabs.concat(tabs.includes('artist') ? ['timeline'] : [], tabs.includes('album') ? ['credits'] : [])
// As soon as a track starts, ask for everything its tabs will show, so it's ready before anyone opens them
let prefetchedFor = ''
export function prefetchInfo(playing: any, tabs: string[]) {
  const key = playing ? [playing.id, playing.artist, playing.album, playing.title].join('|') : ''
  if (!playing || !tabs.length || key === prefetchedFor) return
  prefetchedFor = key
  for (const kind of infoKinds(tabs)) infoQuery(kind, playing).catch(() => {})
}
