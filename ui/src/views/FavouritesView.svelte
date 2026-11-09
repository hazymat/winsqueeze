<script lang="ts">
  import { t, tn } from '../lib/i18n'
  import { untrack } from 'svelte'
  import { selectable } from '../lib/selectable.svelte'
  import type { SelectionAction } from '../lib/selection.svelte'
  import { live } from '../bridge'
  import { infoFor, splitName, detail, itemImage, originLabel } from '../lib/xmlBrowse'
  import { app, ui, openView, toast } from '../state.svelte'
  import Icon from '../parts/Icon.svelte'
  import PageHead from '../parts/PageHead.svelte'
  let { itemId = '', title = '' }: { itemId?: string; title?: string } = $props()
  let items = $state<any[]>([])
  let loading = $state(true)
  // Lyrion's own favourites, read live (they are small and change from other controllers)
  async function load() { const result = await live(['favorites', 'items', 0, 500, 'want_url:1'].concat(itemId ? [`item_id:${itemId}`] : [])); items = result?.loop_loop ?? []; loading = false; if (itemId) addDates() }
  // Inside a folder (e.g. BBC Sounds episodes) only the menu form carries the date, as a second line of its text
  async function addDates() {
    const menu = await live(['favorites', 'items', 0, 500, 'menu:1', `item_id:${itemId}`], true)
    const dates = new Map<string, string>((menu?.item_loop ?? []).map((entry: any) => [String(entry.params?.item_id ?? ''), String(entry.text ?? '').split('\n')[1] ?? '']))
    if (Array.from(dates.values()).some(Boolean)) items = items.map(item => dates.get(String(item.id)) && !item.date ? Object.assign({}, item, { date: dates.get(String(item.id)) }) : item)
  }
  // Reload only when the folder or the server really changes, not on every status push (which made the list flicker)
  const serverName = $derived(String(app.status?.server ?? ''))
  $effect(() => { itemId; serverName; untrack(load) })
  const plain = (text: string) => (text ?? '').toLowerCase()
  const shown = $derived(ui.search.length >= 2 ? items.filter(item => plain(item.name).includes(plain(ui.search))) : items)
  async function activate(item: any) {
    if (item.hasitems && !item.isaudio) openView({ kind: 'favourites', key: item.id, title: splitName(item)[0] })
    else { await live(['favorites', 'playlist', 'play', `item_id:${item.id}`], true); toast(t('Playing {items}', { items: item.name })) }
  }
  async function remove(item: any) { await live(['favorites', 'delete', `item_id:${item.id}`]); toast(t('Removed {item} from favourites', { item: item.name })); load() }
  // Choosing favourites: Lyrion plays them itself (they can be folders, streams or playlists)
  const byId = (keys: string[]) => keys.map(key => items.find(item => String(item.id) === key)).filter(Boolean)
  async function queueFavourites(keys: string[], mode: 'play' | 'insert' | 'add') {
    const chosen = byId(keys)
    for (const [position, item] of chosen.entries()) await live(['favorites', 'playlist', mode === 'play' && position > 0 ? 'add' : mode, `item_id:${item.id}`], true)
    toast(mode === 'play' ? t('Playing {items}', { items: chosen.length === 1 ? chosen[0].name : t('{count} favourites', { count: chosen.length }) }) : mode === 'insert' ? t('Playing next') : t('Added to Now playing'))
  }
  async function removeFavourites(keys: string[]) {
    const chosen = byId(keys)
    for (const item of chosen.slice().sort((first, second) => String(second.id).localeCompare(String(first.id)))) await live(['favorites', 'delete', `item_id:${item.id}`])
    load()
    toast(chosen.length === 1 ? t('Removed {item} from favourites', { item: chosen[0].name }) : t('Removed {count} favourites', { count: chosen.length }), async () => { for (const item of chosen) if (item.url) await live(['favorites', 'add', `url:${item.url}`, `title:${item.name}`]); load() })
  }
  const favouriteActions: SelectionAction[] = [
    { label: t('Play now'), key: 'Enter', run: keys => queueFavourites(keys, 'play') },
    { label: t('Play next'), run: keys => queueFavourites(keys, 'insert') },
    { label: t('Add to end'), run: keys => queueFavourites(keys, 'add') },
    { label: t('Info'), run: keys => { ui.itemInfo = byId(keys).map(infoFor) } },
    { label: t('Remove from favourites'), key: 'Delete', danger: true, run: removeFavourites }
  ]
</script>

<PageHead title={itemId ? title || t('Favourites') : t('Favourites')} detail={items.length ? tn(items.length, '{count} item', '{count} items') : ''} />
{#if loading}<p class="quiet">{t('Loading…')}</p>{:else if !items.length}<p class="quiet">{t('No favourites yet. Star an album to add it.')}</p>{/if}
<div class="favourites" use:selectable={{ scope: 'favourites:' + itemId, noun: 'favourite', actions: favouriteActions }}>
  {#each shown as item (item.id)}
    <div class="favourite" data-select-key={String(item.id)}>
      <button class="main" onclick={() => activate(item)}>
        {#if itemImage(item)}<img src={itemImage(item)} alt="" loading="lazy" />{:else}<span class="glyph">{item.hasitems && !item.isaudio ? '📁' : item.type === 'audio' ? '📻' : '★'}</span>{/if}
        <span class="text" title={item.name}><span class="name">{splitName(item)[0]}</span>{#if detail(item)}<span class="detail">{detail(item)}</span>{/if}{#if !itemId && originLabel(item)}<span class="origin">{originLabel(item)}</span>{/if}</span>
        <span class="kind" title={item.hasitems && !item.isaudio ? t('Open') : t('Play')}><Icon name={item.hasitems && !item.isaudio ? 'folders' : 'play'} size={16} /></span>
      </button>
      <button class="remove" onclick={() => remove(item)} title={t('Remove from favourites')}>★</button>
    </div>
  {/each}
</div>

<style>
  .favourites { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 4px 16px; }
  .favourite { display: flex; align-items: center; border-radius: 8px; }
  .favourite:hover { background: var(--hover); }
  .main { flex: 1; min-width: 0; display: flex; align-items: center; gap: 10px; background: none; border: 0; color: var(--fg); padding: 6px 8px; cursor: pointer; font: inherit; text-align: left; }
  .main img, .glyph { width: 40px; height: 40px; border-radius: 6px; object-fit: cover; display: grid; place-items: center; background: var(--panel-strong); flex-shrink: 0; }
  .text { flex: 1; min-width: 0; display: flex; flex-direction: column; } .detail { color: var(--fg-quiet); font-size: 12px; display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 2; overflow: hidden; line-height: 1.3; }
  .origin { color: var(--fg-quiet); font-size: 10.5px; letter-spacing: 0.04em; text-transform: uppercase; opacity: 0.75; margin-top: 1px; }
  .name { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .kind { color: var(--fg-quiet); display: grid; place-items: center; opacity: 0.7; } .favourite:hover .kind { color: var(--accent); opacity: 1; }
  .remove { background: none; border: 0; color: var(--accent); cursor: pointer; font-size: 16px; padding: 0 10px; }
  .quiet { color: var(--fg-quiet); }
</style>
