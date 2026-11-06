import { serverArt } from '../bridge'
import { formatTime } from '../state.svelte'
// Lyrion's plugin menus (favourites, radio, apps) all share one item shape; these read it the same way everywhere
const labels: Record<string, string> = { name: 'Name', title: 'Title', line1: 'Title', line2: 'Detail', subtitle: 'Detail', description: 'Description', date: 'Date', year: 'Year', duration: 'Length', artist: 'Artist', album: 'Album', remote_title: 'Station', type: 'Type', url: 'Address' }
const formatValue = (key: string, value: any) => key === 'duration' && Number(value) > 0 ? formatTime(Number(value)) : String(value)
// Whatever the server or plugin (e.g. BBC Sounds) gives for an item: date, length, description…
export const infoFor = (item: any) => ({ name: item.name, fields: Object.keys(item).filter(key => !['id', 'hasitems', 'isaudio', 'image', 'icon'].includes(key) && item[key] !== '' && item[key] != null && typeof item[key] !== 'object').map(key => [labels[key] ?? key, formatValue(key, item[key])] as [string, string]) })
// "Programme - what it is" names (BBC Sounds) split into a title and a subtitle line
export const splitName = (item: any) => { const name = String(item.name ?? ''), cut = name.indexOf(' - '); return cut > 0 ? [name.slice(0, cut), name.slice(cut + 3)] : [name, ''] }
export const detail = (item: any) => [item.line2 ?? item.subtitle ?? splitName(item)[1], /^\d{1,2}\/\d{1,2}\/\d{4}$/.test(String(item.name)) ? '' : item.date ?? '', Number(item.duration) > 0 ? formatTime(Number(item.duration)) : ''].filter(Boolean).join(' · ') || (item.description ?? '')
export const itemImage = (item: any, size = 120) => serverArt(String(item.image ?? item.icon ?? ''), size)
export const isFolder = (item: any) => (item.hasitems && !item.isaudio) || item.type === 'link' || item.type === 'outline'
