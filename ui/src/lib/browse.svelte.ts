import { app, readSetting, writeSetting } from '../state.svelte'
import { sendToShell } from '../bridge'
import { t, language } from './i18n'
// Second-level browsing: how each area sorts, groups and sizes its albums (SPEC.md › Browsing)
export const sortOptions: [string, string][] = [['artist', t('Album artist')], ['title', t('Title')], ['year', t('Year')], ['added', t('Recently added')]]
export const groupOptions: [string, string][] = [['none', t('None')], ['artist', t('Album artist')], ['year', t('Year')], ['decade', t('Decade')], ['added', t('Date added')], ['format', t('Format')], ['initial', t('First letter')]]
export const sizeOptions: [string, string][] = [['large', t('Large')], ['compact', t('Compact')], ['list', t('List')]]
export const builtInGroupDefaults: Record<string, string> = { genre: 'artist', artist: 'none', composer: 'artist', performer: 'artist', albums: 'none', custom: 'artist', new: 'added', updated: 'added', random: 'none', compilations: 'none' }
// Custom views (SPEC.md › Custom views): the steps a view can take before its albums
export const levelNames: Record<string, string> = { genre: t('Genres'), composer: t('Composers'), artist: t('Artists'), performer: t('Performers') }
export const customViewsExplainer = t('The built-in lists browse your whole library one way: pick a composer, see their albums. A custom view first narrows the library to genres you choose, then steps through the order you choose, such as Genre, then Composer, then albums. So a Classical view lists only classical composers, without the pop songwriters credited on your other albums, and lets you ask questions the built-in lists can\'t, like which composers you have in Choral, or which genres you have of Bach.')
// Saved at once into settings (export/import carries them); order is the order on a sidebar menu's page
export function saveCustomViews(views: any[]) { app.librarySettings.CustomViews = JSON.parse(JSON.stringify(views)); sendToShell({ type: 'setCustomViews', views: app.librarySettings.CustomViews }) }
// Moves one view among those shown together (a sidebar menu's views), keeping every other view where it is
export function moveCustomView(shownIds: string[], from: number, to: number) {
  const all: any[] = app.librarySettings.CustomViews ?? [], reordered = shownIds.slice(), moved = reordered.splice(from, 1)[0]
  reordered.splice(to, 0, moved)
  let next = 0
  saveCustomViews(all.map(view => shownIds.includes(view.Id) ? all.find(each => each.Id === reordered[next++]) : view))
}
const monthYear = new Intl.DateTimeFormat(language, { month: 'long', year: 'numeric' })

// Groupings offered in the toolbar (Settings › Browsing can trim the list); None is always there
export function offeredGroups(): [string, string][] {
  const offered: string[] | undefined = app.librarySettings.BrowseGroupings
  return groupOptions.filter(([value]) => value === 'none' || !offered || offered.includes(value))
}

export function defaultGroup(area: string) { return app.librarySettings.DefaultGrouping?.[area] ?? builtInGroupDefaults[area] ?? 'none' }

// Per-area choice, remembered once changed; an unchanged area follows the default from Settings
export function browseState(area: string) {
  const state = $state({ sort: readSetting('sort.' + area, 'artist'), chosenGroup: readSetting('group.' + area, ''), size: readSetting('size.' + area, readSetting('layout.' + area, 'grid') === 'list' ? 'list' : 'large') })
  return {
    get sort() { return state.sort }, set sort(value: string) { state.sort = value; writeSetting('sort.' + area, value) },
    get group() { const chosen = state.chosenGroup || defaultGroup(area); return offeredGroups().some(([value]) => value === chosen) ? chosen : 'none' }, set group(value: string) { state.chosenGroup = value; writeSetting('group.' + area, value) },
    get size() { return state.size }, set size(value: string) { state.size = value; writeSetting('size.' + area, value) },
  }
}

function groupKey(album: any, group: string): [string, string] {
  const year = Number(album.year) || 0
  switch (group) {
    case 'artist': return [String(album.artist || t('Unknown artist')).toLowerCase(), album.artist || t('Unknown artist')]
    case 'year': return year ? [String(year).padStart(4, '0'), String(year)] : ['9999', t('Unknown year')]
    case 'decade': return year ? [String(Math.floor(year / 10) * 10), t('{decade}s', { decade: Math.floor(year / 10) * 10 })] : ['9999', t('Unknown year')]
    case 'added': { if (!album.added) return ['0', t('Unknown date')]; const addedDate = new Date(album.added * 1000); return [String(addedDate.getFullYear() * 100 + addedDate.getMonth()).padStart(6, '0'), monthYear.format(addedDate)] }
    case 'format': { const formatName = String(album.format || t('Unknown format')).replace(/ \(mixed\)$/, ''); return [formatName.toLowerCase(), formatName] }
    case 'initial': { const letter = String(album.title || '').normalize('NFD').replace(/^(the|a|an)\s+/i, '').charAt(0).toUpperCase(); return /[A-Z]/.test(letter) ? [letter, letter] : ['~', '#'] }
  }
  return ['', '']
}

// Albums under headings; keeps the server's sort order inside each group. Date added runs newest first, everything else A to Z / oldest first
export function groupAlbums(albums: any[], group: string): { key: string; heading: string; albums: any[] }[] {
  if (group === 'none') return [{ key: '', heading: '', albums }]
  const groups = new Map<string, { key: string; heading: string; albums: any[] }>()
  for (const album of albums) { const [key, heading] = groupKey(album, group); if (!groups.has(key)) groups.set(key, { key, heading, albums: [] }); groups.get(key)!.albums.push(album) }
  const ordered = Array.from(groups.values()).sort((first, second) => first.key.localeCompare(second.key, undefined, { numeric: true }))
  return group === 'added' ? ordered.reverse() : group === 'artist' ? mergeSingles(ordered) : ordered
}
// Runs of one-album artists share one group headed by their letter range ("A–E · 14 artists"); the card's artist line names each
function mergeSingles(groups: { key: string; heading: string; albums: any[] }[]) {
  const merged: { key: string; heading: string; albums: any[] }[] = []
  let run: { key: string; heading: string; albums: any[] }[] = []
  const initial = (heading: string) => (heading.trim()[0] ?? '#').toUpperCase()
  const flush = () => {
    if (run.length === 1) merged.push(run[0])
    else if (run.length > 1) { const first = initial(run[0].heading), last = initial(run[run.length - 1].heading); merged.push({ key: run[0].key + '…', heading: `${first === last ? first : first + '–' + last} · ${t('{count} artists', { count: run.length })}`, albums: run.flatMap(group => group.albums) }) }
    run = []
  }
  for (const group of groups) { if (group.albums.length === 1) run.push(group); else { flush(); merged.push(group) } }
  flush()
  return merged
}
