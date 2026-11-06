import { serverArt } from '../bridge'
import { formatTime } from '../state.svelte'
// Lyrion's plugin menus (favourites, radio, apps) all share one item shape; these read it the same way everywhere
const labels: Record<string, string> = { name: 'Name', title: 'Title', line1: 'Title', line2: 'Detail', subtitle: 'Detail', description: 'Description', date: 'Date', year: 'Year', duration: 'Length', artist: 'Artist', album: 'Album', remote_title: 'Station', type: 'Type', url: 'Address' }
const formatValue = (key: string, value: any) => key === 'duration' && Number(value) > 0 ? formatTime(Number(value)) : String(value)
// What an item is and where it comes from, read from its address (Lyrion's own "type" says audio for nearly everything)
const pluginBySchema: Record<string, string> = { sounds: 'BBC Sounds', soundslist: 'BBC Sounds', randomplay: 'Random Mix', spotify: 'Spotify', tidal: 'TIDAL', qobuz: 'Qobuz', deezer: 'Deezer', radioparadise: 'Radio Paradise', youtube: 'YouTube', podcast: 'Podcasts', mixcloud: 'Mixcloud', bandcamp: 'Bandcamp', file: 'Library', db: 'Library' }
export function origin(item: any) {
  const url = String(item.url ?? ''), scheme = (url.match(/^([a-z0-9]+):/i)?.[1] ?? '').toLowerCase(), folder = item.hasitems && !item.isaudio
  const pluginFolder = String(item.image ?? item.icon ?? '').match(/plugins\/([^/]+)\//)?.[1]?.replace(/([a-z])([A-Z])/g, '$1 $2')
  let source = pluginBySchema[scheme] ?? ''
  if (!source && /^https?$/.test(scheme)) { try { source = new URL(url).hostname.replace(/^www\./, '') } catch { source = '' } }
  if (!source && pluginFolder) source = pluginFolder
  const kind = scheme === 'soundslist' ? 'Programme' : scheme === 'sounds' ? (url.includes('_LIVE_') ? 'Live station' : 'Episode') : scheme === 'randomplay' ? 'Random mix'
    : scheme === 'db' ? (url.includes('album') ? 'Album' : url.includes('contributor') ? 'Artist' : 'Library item') : scheme === 'file' ? (/\.(m3u8?|pls|cue)$/i.test(url) ? 'Playlist' : /\/$/.test(url) ? 'Folder' : 'Track')
    : /^https?$/.test(scheme) ? (folder ? 'Feed' : 'Stream') : folder ? 'Folder' : item.type === 'playlist' ? 'Playlist' : ''
  return { kind, source }
}
export const originLabel = (item: any) => { const { kind, source } = origin(item); return (kind.toLowerCase() === source.toLowerCase() ? [source] : [source, kind]).filter(Boolean).join(' · ') }
// Whatever the server or plugin (e.g. BBC Sounds) gives for an item: date, length, description…
export const infoFor = (item: any) => ({ name: item.name, fields: ([['Kind', origin(item).kind], ['Source', origin(item).source]] as [string, string][]).filter(([label, value]) => value).concat(Object.keys(item).filter(key => !['id', 'hasitems', 'isaudio', 'image', 'icon', 'type'].includes(key) && item[key] !== '' && item[key] != null && typeof item[key] !== 'object').map(key => [labels[key] ?? key, formatValue(key, item[key])] as [string, string])) })
// "Programme - what it is" names (BBC Sounds) split into a title and a subtitle line
export const splitName = (item: any) => { const name = String(item.name ?? ''), cut = name.indexOf(' - '); return cut > 0 ? [name.slice(0, cut), name.slice(cut + 3)] : [name, ''] }
export const detail = (item: any) => [item.line2 ?? item.subtitle ?? splitName(item)[1], /^\d{1,2}\/\d{1,2}\/\d{4}$/.test(String(item.name)) ? '' : item.date ?? '', Number(item.duration) > 0 ? formatTime(Number(item.duration)) : ''].filter(Boolean).join(' · ') || (item.description ?? '')
export const itemImage = (item: any, size = 120) => serverArt(String(item.image ?? item.icon ?? ''), size)
export const isFolder = (item: any) => (item.hasitems && !item.isaudio) || item.type === 'link' || item.type === 'outline'
