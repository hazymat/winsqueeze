<script lang="ts">
  import { untrack } from 'svelte'
  import { selectable } from '../lib/selectable.svelte'
  import type { SelectionAction } from '../lib/selection.svelte'
  import { live, artUrl } from '../bridge'
  import { app, ui, openView, toast, formatTime } from '../state.svelte'
  let { itemId = '' }: { itemId?: string } = $props()
  let items = $state<any[]>([])
  let loading = $state(true)
  // Lyrion's own favourites, read live (they are small and change from other controllers)
  async function load() { const result = await live(['favorites', 'items', 0, 500, 'want_url:1'].concat(itemId ? [`item_id:${itemId}`] : [])); items = result?.loop_loop ?? []; loading = false }
  // Reload only when the folder or the server really changes, not on every status push (which made the list flicker)
  const serverName = $derived(String(app.status?.server ?? ''))
  $effect(() => { itemId; serverName; untrack(load) })
  const plain = (text: string) => (text ?? '').toLowerCase()
  const shown = $derived(ui.search.length >= 2 ? items.filter(item => plain(item.name).includes(plain(ui.search))) : items)
  async function activate(item: any) {
    if (item.hasitems && !item.isaudio) openView({ kind: 'favourites', key: item.id, title: item.name })
    else { await live(['favorites', 'playlist', 'play', `item_id:${item.id}`], true); toast(`Playing ${item.name}`) }
  }
  async function remove(item: any) { await live(['favorites', 'delete', `item_id:${item.id}`]); toast(`Removed ${item.name} from favourites`); load() }
  // Choosing favourites: Lyrion plays them itself (they can be folders, streams or playlists)
  const byId = (keys: string[]) => keys.map(key => items.find(item => String(item.id) === key)).filter(Boolean)
  async function queueFavourites(keys: string[], mode: 'play' | 'insert' | 'add') {
    const chosen = byId(keys)
    for (const [position, item] of chosen.entries()) await live(['favorites', 'playlist', mode === 'play' && position > 0 ? 'add' : mode, `item_id:${item.id}`], true)
    toast(mode === 'play' ? `Playing ${chosen.length === 1 ? chosen[0].name : chosen.length + ' favourites'}` : mode === 'insert' ? 'Playing next' : 'Added to Now playing')
  }
  async function removeFavourites(keys: string[]) {
    const chosen = byId(keys)
    for (const item of chosen.slice().sort((first, second) => String(second.id).localeCompare(String(first.id)))) await live(['favorites', 'delete', `item_id:${item.id}`])
    load()
    toast(chosen.length === 1 ? `Removed ${chosen[0].name} from favourites` : `Removed ${chosen.length} favourites`, async () => { for (const item of chosen) if (item.url) await live(['favorites', 'add', `url:${item.url}`, `title:${item.name}`]); load() })
  }
  const favouriteActions: SelectionAction[] = [
    { label: 'Play now', key: 'Enter', run: keys => queueFavourites(keys, 'play') },
    { label: 'Play next', run: keys => queueFavourites(keys, 'insert') },
    { label: 'Add to end', run: keys => queueFavourites(keys, 'add') },
    { label: 'Info', run: keys => { ui.itemInfo = byId(keys).map(infoFor) } },
    { label: 'Remove from favourites', key: 'Delete', danger: true, run: removeFavourites }
  ]
  // Whatever the server or plugin (e.g. BBC Sounds) gives for an item: date, length, description…
  const labels: Record<string, string> = { name: 'Name', title: 'Title', line1: 'Title', line2: 'Detail', subtitle: 'Detail', description: 'Description', date: 'Date', year: 'Year', duration: 'Length', artist: 'Artist', album: 'Album', remote_title: 'Station', type: 'Type', url: 'Address' }
  const formatValue = (key: string, value: any) => key === 'duration' && Number(value) > 0 ? formatTime(Number(value)) : String(value)
  const infoFor = (item: any) => ({ name: item.name, fields: Object.keys(item).filter(key => !['id', 'hasitems', 'isaudio', 'image', 'icon'].includes(key) && item[key] !== '' && item[key] != null && typeof item[key] !== 'object').map(key => [labels[key] ?? key, formatValue(key, item[key])] as [string, string]) })
  const detail = (item: any) => [item.line2 ?? item.subtitle ?? '', item.date ?? '', Number(item.duration) > 0 ? formatTime(Number(item.duration)) : ''].filter(Boolean).join(' · ') || (item.description ?? '')
  const image = (item: any) => { const source = String(item.image ?? item.icon ?? ''); return source.startsWith('http') ? source : source.startsWith('/') || source.startsWith('imageproxy') || source.startsWith('html/') || source.startsWith('plugins/') ? `https://art.winsqueeze/server/${encodeURIComponent(source.replace(/^\//, ''))}` : '' }
</script>

{#if !itemId}<h2>Favourites</h2>{/if}
{#if loading}<p class="quiet">Loading…</p>{:else if !items.length}<p class="quiet">No favourites yet. Star an album to add it.</p>{/if}
<div class="favourites" use:selectable={{ scope: 'favourites:' + itemId, noun: 'favourite', actions: favouriteActions }}>
  {#each shown as item (item.id)}
    <div class="favourite" data-select-key={String(item.id)}>
      <button class="main" onclick={() => activate(item)}>
        {#if image(item)}<img src={image(item)} alt="" loading="lazy" />{:else}<span class="glyph">{item.hasitems && !item.isaudio ? '📁' : item.type === 'audio' ? '📻' : '★'}</span>{/if}
        <span class="text"><span class="name">{item.name}</span>{#if detail(item)}<span class="detail">{detail(item)}</span>{/if}</span>
        <span class="kind">{item.hasitems && !item.isaudio ? 'Open' : 'Play'}</span>
      </button>
      <button class="remove" onclick={() => remove(item)} title="Remove from favourites">★</button>
    </div>
  {/each}
</div>

<style>
  h2 { margin: 0 0 12px; font-size: 24px; }
  .favourites { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 4px 16px; }
  .favourite { display: flex; align-items: center; border-radius: 8px; }
  .favourite:hover { background: var(--hover); }
  .main { flex: 1; min-width: 0; display: flex; align-items: center; gap: 10px; background: none; border: 0; color: var(--fg); padding: 6px 8px; cursor: pointer; font: inherit; text-align: left; }
  .main img, .glyph { width: 40px; height: 40px; border-radius: 6px; object-fit: cover; display: grid; place-items: center; background: var(--panel-strong); flex-shrink: 0; }
  .text { flex: 1; min-width: 0; display: flex; flex-direction: column; } .detail { color: var(--fg-quiet); font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .name { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .kind { color: var(--fg-quiet); font-size: 12px; }
  .remove { background: none; border: 0; color: var(--accent); cursor: pointer; font-size: 16px; padding: 0 10px; }
  .quiet { color: var(--fg-quiet); }
</style>
