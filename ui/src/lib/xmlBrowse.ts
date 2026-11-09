import { serverArt } from '../bridge'
import { formatTime } from '../state.svelte'
import { t } from './i18n'
// Lyrion's plugin menus (favourites, radio, apps) all share one item shape; these read it the same way everywhere
const labels: Record<string, string> = { name: t('Name'), title: t('Title'), line1: t('Title'), line2: t('Detail'), subtitle: t('Detail'), description: t('Description'), date: t('Date'), year: t('Year'), duration: t('Length'), artist: t('Artist'), album: t('Album'), remote_title: t('Station'), type: t('Type'), url: t('Address') }
const formatValue = (key: string, value: any) => key === 'duration' && Number(value) > 0 ? formatTime(Number(value)) : String(value)
// What an item is and where it comes from, read from its address (Lyrion's own "type" says audio for nearly everything)
const pluginBySchema: Record<string, string> = { sounds: 'BBC Sounds', soundslist: 'BBC Sounds', randomplay: 'Random Mix', spotify: 'Spotify', tidal: 'TIDAL', qobuz: 'Qobuz', deezer: 'Deezer', radioparadise: 'Radio Paradise', youtube: 'YouTube', podcast: t('Podcasts'), mixcloud: 'Mixcloud', bandcamp: 'Bandcamp', file: t('Library'), db: t('Library') }
export function origin(item: any) {
  const url = String(item.url ?? ''), scheme = (url.match(/^([a-z0-9]+):/i)?.[1] ?? '').toLowerCase(), folder = item.hasitems && !item.isaudio
  const pluginFolder = String(item.image ?? item.icon ?? '').match(/plugins\/([^/]+)\//)?.[1]?.replace(/([a-z])([A-Z])/g, '$1 $2')
  let source = pluginBySchema[scheme] ?? ''
  if (!source && /^https?$/.test(scheme)) { try { source = new URL(url).hostname.replace(/^www\./, '') } catch { source = '' } }
  if (!source && pluginFolder) source = pluginFolder
  const kind = scheme === 'soundslist' ? t('Programme') : scheme === 'sounds' ? (url.includes('_LIVE_') ? t('Live station') : t('Episode')) : scheme === 'randomplay' ? t('Random mix')
    : scheme === 'db' ? (url.includes('album') ? t('Album') : url.includes('contributor') ? t('Artist') : t('Library item')) : scheme === 'file' ? (/\.(m3u8?|pls|cue)$/i.test(url) ? t('Playlist') : /\/$/.test(url) ? t('Folder') : t('Track'))
    : /^https?$/.test(scheme) ? (folder ? t('Feed') : t('Stream')) : folder ? t('Folder') : item.type === 'playlist' ? t('Playlist') : ''
  return { kind, source }
}
export const originLabel = (item: any) => { const { kind, source } = origin(item); return (kind.toLowerCase() === source.toLowerCase() ? [source] : [source, kind]).filter(Boolean).join(' · ') }
// Whatever the server or plugin (e.g. BBC Sounds) gives for an item: date, length, description…
export const infoFor = (item: any) => ({ name: item.name, fields: ([[t('Kind'), origin(item).kind], [t('Source'), origin(item).source]] as [string, string][]).filter(([label, value]) => value).concat(Object.keys(item).filter(key => !['id', 'hasitems', 'isaudio', 'image', 'icon', 'type'].includes(key) && item[key] !== '' && item[key] != null && typeof item[key] !== 'object').map(key => [labels[key] ?? key, formatValue(key, item[key])] as [string, string])) })
// "Programme - what it is" names (BBC Sounds) split into a title and a subtitle line
export const splitName = (item: any) => { const name = String(item.name ?? ''), cut = name.indexOf(' - '); return cut > 0 ? [name.slice(0, cut), name.slice(cut + 3)] : [name, ''] }
export const detail = (item: any) => [item.line2 ?? item.subtitle ?? splitName(item)[1], /^\d{1,2}\/\d{1,2}\/\d{4}$/.test(String(item.name)) ? '' : item.date ?? '', Number(item.duration) > 0 ? formatTime(Number(item.duration)) : ''].filter(Boolean).join(' · ') || (item.description ?? '')
export const itemImage = (item: any, size = 120) => serverArt(String(item.image ?? item.icon ?? ''), size)
export const isFolder = (item: any) => (item.hasitems && !item.isaudio) || item.type === 'link' || item.type === 'outline'
