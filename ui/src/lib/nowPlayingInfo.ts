import { query } from '../bridge'
import { app } from '../state.svelte'
// The server's sources (tags, Music & Artist Information plugin) read the music folder and can wake a sleeping drive (#383), so like Lyrion's own web pages they're asked only for what's on screen: the tab or Lyrics window being looked at, once something has played since the app opened. Every other lookup is online only
let playedSinceOpening = false
export const hasPlayed = () => playedSinceOpening || (playedSinceOpening = app.nowPlaying?.mode === 'play')
// Radio and other streams: flagged by Lyrion, or a negative (in-memory) track id, or anything that isn't a file
export const isStream = (playing: any) => Number(playing?.remote) === 1 || Number(playing?.id ?? 0) < 0 || (!!playing?.url && !String(playing.url).startsWith('file:'))
// Now playing views (SPEC.md › Now playing views): one lookup per kind for the playing track; the shell keeps answers for the session, so asking early makes the tabs instant
export const infoQuery = (kind: string, playing: any, onScreen = false) => query('nowPlayingInfo', { kind, id: playing.id ?? 0, artistId: String(playing.artist_id ?? ''), albumId: String(playing.album_id ?? ''), artist: playing.artist ?? '', album: playing.album ?? '', title: playing.title ?? '', duration: Number(playing.duration ?? 0), remote: isStream(playing) ? 1 : 0, onlineOnly: onScreen && hasPlayed() ? 0 : 1 })
// The kinds the ticked tabs need: each tab, plus Timeline with Artist and Credits with Album
export const infoKinds = (tabs: string[]) => tabs.concat(tabs.includes('artist') ? ['timeline'] : [], tabs.includes('album') ? ['credits'] : [])
// As soon as a track starts, ask the online sources for everything its tabs will show, so it's ready before anyone opens them
let prefetchedFor = ''
export function prefetchInfo(playing: any, tabs: string[]) {
  const key = playing ? [playing.id, playing.artist, playing.album, playing.title].join('|') : ''
  if (!playing || !tabs.length || key === prefetchedFor) return
  prefetchedFor = key
  for (const kind of infoKinds(tabs)) infoQuery(kind, playing).catch(() => {})
}
