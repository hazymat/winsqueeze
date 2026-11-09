<script lang="ts">
  import { t } from '../lib/i18n'
  import { untrack } from 'svelte'
  import { selectable } from '../lib/selectable.svelte'
  import type { SelectionAction } from '../lib/selection.svelte'
  import { live } from '../bridge'
  import { app, ui, openView, toast } from '../state.svelte'
  import { infoFor, splitName, detail, itemImage, isFolder } from '../lib/xmlBrowse'
  import Icon from '../parts/Icon.svelte'
  import PinButton from '../parts/PinButton.svelte'
  import PageHead from '../parts/PageHead.svelte'
  // Radio and Apps (SPEC.md › Radio and apps): any plugin the server offers, browsed through Lyrion's shared menu format, so no plugin needs its own support.
  // Top level: kind 'radio' or 'apps' lists the server's own list. Inside: kind 'app' (key "cmd|itemId") browses; kind 'app-search' (same key) asks for words first.
  let { kind, key = '', title = '' }: { kind: string, key?: string, title?: string } = $props()
  const cmd = $derived(key.split('|')[0]), itemId = $derived(key.split('|').slice(1).join('|'))
  let items = $state<any[]>([])
  let loading = $state(true), failed = $state(false)
  let searchWords = $state(''), searched = $state('')
  async function load() {
    loading = true; failed = false
    try {
      if (kind === 'radio' || kind === 'apps') { const result = await live([kind === 'radio' ? 'radios' : 'apps', 0, 500], true); items = (result?.[kind === 'radio' ? 'radioss_loop' : 'appss_loop'] ?? []).filter((entry: any) => entry.cmd) }
      else if (kind === 'app-search' && !searched) items = []
      else { const result = await live([cmd, 'items', 0, 500, 'want_url:1'].concat(itemId ? [`item_id:${itemId}`] : [], searched ? [`search:${searched}`] : []), true); items = result?.loop_loop ?? []; failed = !result }
    }
    catch { failed = true }
    loading = false
  }
  const serverName = $derived(String(app.status?.server ?? ''))
  $effect(() => { kind; key; serverName; untrack(load) })
  const plain = (text: string) => (text ?? '').toLowerCase()
  const shown = $derived(ui.search.length >= 2 ? items.filter(item => plain(item.name).includes(plain(ui.search))) : items)
  const top = $derived(kind === 'radio' || kind === 'apps')
  const heading = $derived(kind === 'radio' ? t('Radio') : kind === 'apps' ? t('Apps') : title)
  function activate(item: any) {
    if (top) openView({ kind: item.type === 'xmlbrowser_search' ? 'app-search' : 'app', key: `${item.cmd}|`, title: item.name })
    else if (item.type === 'search') openView({ kind: 'app-search', key: `${cmd}|${item.id}`, title: splitName(item)[0] })
    else if (isFolder(item)) openView({ kind: 'app', key: `${cmd}|${item.id}`, title: splitName(item)[0] })
    else if (item.isaudio || item.type === 'audio' || item.type === 'playlist') queueItems([String(item.id)], 'play')
  }
  const byId = (keys: string[]) => keys.map(each => items.find(item => String(item.id) === each)).filter(Boolean)
  // Lyrion plays plugin items itself (streams, episodes, whole albums or playlists)
  async function queueItems(keys: string[], mode: 'play' | 'insert' | 'add') {
    const chosen = byId(keys)
    for (const [position, item] of chosen.entries()) await live([cmd, 'playlist', mode === 'play' && position > 0 ? 'add' : mode, `item_id:${item.id}`], true)
    toast(mode === 'play' ? t('Playing {items}', { items: chosen.length === 1 ? splitName(chosen[0])[0] : t('{count} items', { count: chosen.length }) }) : mode === 'insert' ? t('Playing next') : t('Added to Now playing'))
  }
  async function favourite(keys: string[]) {
    const chosen = byId(keys).filter(item => item.url)
    for (const item of chosen) await live(['favorites', 'add', `url:${item.url}`, `title:${item.name}`].concat(item.image ? [`icon:${item.image}`] : []))
    toast(chosen.length ? t('Added {items} to favourites', { items: chosen.length === 1 ? splitName(chosen[0])[0] : t('{count} items', { count: chosen.length }) }) : t('This app doesn’t let that be a favourite'))
  }
  const itemActions: SelectionAction[] = [
    { label: t('Play now'), key: 'Enter', run: keys => queueItems(keys, 'play') },
    { label: t('Play next'), run: keys => queueItems(keys, 'insert') },
    { label: t('Add to end'), run: keys => queueItems(keys, 'add') },
    { label: t('Add to favourites'), run: favourite },
    { label: t('Info'), run: keys => { ui.itemInfo = byId(keys).map(infoFor) } }
  ]
  const playable = (item: any) => item.isaudio || item.type === 'audio' || item.type === 'playlist'
</script>

<PageHead title={heading}>{#snippet titleExtra()}{#if !top && kind === 'app'}<PinButton view={{ kind, key, title }} />{/if}{/snippet}</PageHead>
{#if kind === 'app-search'}
  <form class="search" onsubmit={(event) => { event.preventDefault(); searched = searchWords.trim(); if (searched) load() }}>
    <!-- svelte-ignore a11y_autofocus -->
    <input bind:value={searchWords} placeholder={t('Search {name}', { name: title })} autofocus /><button type="submit" disabled={!searchWords.trim()}>{t('Search')}</button>
  </form>
{/if}
{#if loading}<p class="quiet">{t('Loading…')}</p>
{:else if failed}<p class="quiet">{t('The server didn’t answer for this one. Some apps only work once they’re signed in on the server.')}</p>
{:else if !items.length && (kind !== 'app-search' || searched)}<p class="quiet">{top ? (kind === 'radio' ? t('The server has no radio plugins turned on.') : t('The server has no apps turned on. Add them in Lyrion’s settings, under Plugins.')) : searched ? t('Nothing found.') : t('Nothing here.')}</p>{/if}
{#if top}
  <div class="tiles">
    {#each shown as item (item.cmd + item.name)}
      <button class="tile" onclick={() => activate(item)} title={item.name}>
        {#if itemImage(item, 200)}<img src={itemImage(item, 200)} alt="" loading="lazy" />{:else}<span class="glyph">{kind === 'radio' ? '📻' : '▦'}</span>{/if}
        <span class="tile-name">{item.name}</span>
      </button>
    {/each}
  </div>
{:else}
  <div class="items" use:selectable={{ scope: 'app:' + kind + key + searched, noun: 'item', actions: itemActions }}>
    {#each shown as item (item.id)}
      <div class="item" data-select-key={String(item.id)}>
        <button class="main" onclick={() => activate(item)} disabled={!isFolder(item) && !playable(item) && item.type !== 'search'}>
          {#if itemImage(item)}<img src={itemImage(item)} alt="" loading="lazy" />{:else}<span class="glyph">{item.type === 'search' ? '🔍' : isFolder(item) ? '📁' : playable(item) ? '♪' : 'ℹ'}</span>{/if}
          <span class="text" title={item.name}><span class="name">{splitName(item)[0]}</span>{#if detail(item)}<span class="detail">{detail(item)}</span>{/if}</span>
          {#if isFolder(item) || playable(item)}<span class="kind" title={isFolder(item) ? t('Open') : t('Play')}><Icon name={isFolder(item) ? 'folders' : 'play'} size={16} /></span>{/if}
        </button>
      </div>
    {/each}
  </div>
{/if}

<style>
  .search { display: flex; gap: 8px; margin: 0 0 14px; max-width: 520px; }
  .search input { flex: 1; min-width: 0; padding: 8px 10px; border-radius: 8px; border: 1px solid var(--line, var(--panel-strong)); background: var(--panel-strong); color: var(--fg); font: inherit; }
  .search button { padding: 8px 14px; border-radius: 8px; border: 0; background: var(--accent); color: var(--on-accent); font: inherit; cursor: pointer; } .search button:disabled { opacity: 0.5; cursor: default; }
  .tiles { display: grid; grid-template-columns: repeat(auto-fill, minmax(130px, 1fr)); gap: 14px; }
  .tile { display: flex; flex-direction: column; align-items: center; gap: 8px; background: none; border: 0; color: var(--fg); padding: 10px; border-radius: 10px; cursor: pointer; font: inherit; }
  .tile:hover { background: var(--hover); }
  .tile img, .tile .glyph { width: 96px; height: 96px; border-radius: 12px; object-fit: contain; display: grid; place-items: center; background: var(--panel-strong); font-size: 36px; }
  .tile-name { font-size: 13px; text-align: center; display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 2; overflow: hidden; }
  .items { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 4px 16px; }
  .item { display: flex; align-items: center; border-radius: 8px; } .item:hover { background: var(--hover); }
  .main { flex: 1; min-width: 0; display: flex; align-items: center; gap: 10px; background: none; border: 0; color: var(--fg); padding: 6px 8px; cursor: pointer; font: inherit; text-align: left; } .main:disabled { cursor: default; }
  .main img, .glyph { width: 40px; height: 40px; border-radius: 6px; object-fit: cover; display: grid; place-items: center; background: var(--panel-strong); flex-shrink: 0; }
  .text { flex: 1; min-width: 0; display: flex; flex-direction: column; } .detail { color: var(--fg-quiet); font-size: 12px; display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 2; overflow: hidden; line-height: 1.3; }
  .name { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .kind { color: var(--fg-quiet); display: grid; place-items: center; opacity: 0.7; } .item:hover .kind { color: var(--accent); opacity: 1; }
  .quiet { color: var(--fg-quiet); }
</style>
