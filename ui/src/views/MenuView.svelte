<script lang="ts">
  import { t } from '../lib/i18n'
  import { untrack } from 'svelte'
  import { selectable } from '../lib/selectable.svelte'
  import type { SelectionAction } from '../lib/selection.svelte'
  import { live, serverArt } from '../bridge'
  import { app, ui, toast } from '../state.svelte'
  import { actionOf, browse, commandOf, menuText, needsWords, openMenuItem, type MenuAction } from '../lib/jiveMenu'
  import Icon from '../parts/Icon.svelte'
  import PinButton from '../parts/PinButton.svelte'
  import PageHead from '../parts/PageHead.svelte'
  // A plugin's own player menu (SPEC.md › Radio and apps › Extras), shown natively: folders open, play entries play, settings-style entries run
  let { key, title = '' }: { key: string, title?: string } = $props()
  const action = $derived(JSON.parse(key) as MenuAction)
  let items = $state<any[]>([]), base = $state<any>(null)
  let loading = $state(true), failed = $state(false)
  let searchWords = $state(''), searched = $state('')
  const asksWords = $derived(needsWords(action))
  async function load() {
    if (asksWords && !searched) { items = []; loading = false; return }
    loading = true; failed = false
    try { const page = await browse(action, searched); items = page.items; base = page.base; failed = !page.items }
    catch { failed = true }
    loading = false
  }
  const serverName = $derived(String(app.status?.server ?? ''))
  $effect(() => { key; serverName; untrack(load) })
  const plain = (text: string) => (text ?? '').toLowerCase()
  const shown = $derived(ui.search.length >= 2 ? items.filter(item => plain(menuText(item).join(' ')).includes(plain(ui.search))) : items)
  const inert = (item: any) => item.style === 'itemNoAction' || item.action === 'none' || !(actionOf(item, 'do', base) ?? actionOf(item, 'go', base))
  const opensPage = (item: any) => !actionOf(item, 'do', base) && !(item.nextWindow ?? actionOf(item, 'go', base)?.nextWindow)
  const icon = (item: any) => serverArt(String(item.icon ?? item['icon-id'] ?? item.window?.['icon-id'] ?? ''), 120)
  async function activate(item: any) { if (!inert(item) && await openMenuItem(item, base) === 'refresh') load() }
  const byIndex = (keys: string[]) => keys.map(each => items[Number(each)]).filter(Boolean)
  // Play now / next / add to end, for entries that offer them
  async function queue(keys: string[], name: 'play' | 'add-hold' | 'add') {
    const chosen = byIndex(keys).filter(item => actionOf(item, name, base))
    for (const item of chosen) await live(commandOf(actionOf(item, name, base)!), true)
    if (chosen.length) toast(name === 'play' ? t('Playing {items}', { items: menuText(chosen[0])[0] }) : name === 'add-hold' ? t('Playing next') : t('Added to Now playing'))
  }
  const itemActions: SelectionAction[] = [
    { label: t('Play now'), key: 'Enter', run: keys => queue(keys, 'play') },
    { label: t('Play next'), run: keys => queue(keys, 'add-hold') },
    { label: t('Add to end'), run: keys => queue(keys, 'add') }
  ]
</script>

<PageHead {title}>{#snippet titleExtra()}{#if !asksWords}<PinButton view={{ kind: 'menu', key, title }} />{/if}{/snippet}</PageHead>
{#if asksWords}
  <form class="search" onsubmit={(event) => { event.preventDefault(); searched = searchWords.trim(); if (searched) load() }}>
    <!-- svelte-ignore a11y_autofocus -->
    <input bind:value={searchWords} placeholder={title} autofocus /><button type="submit" disabled={!searchWords.trim()}>{t('OK')}</button>
  </form>
{/if}
{#if loading}<p class="quiet">{t('Loading…')}</p>
{:else if failed}<p class="quiet">{t('The server didn’t answer for this one. Some apps only work once they’re signed in on the server.')}</p>
{:else if !items.length && (!asksWords || searched)}<p class="quiet">{t('Nothing here.')}</p>{/if}
<div class="items" use:selectable={{ scope: 'menu:' + key + searched, noun: 'item', actions: itemActions }}>
  {#each shown as item, index (index)}
    <div class="item" data-select-key={String(items.indexOf(item))}>
      <button class="main" onclick={() => activate(item)} disabled={inert(item)}>
        {#if icon(item)}<img src={icon(item)} alt="" loading="lazy" />{/if}
        <span class="text" title={menuText(item)[0]}><span class="name">{menuText(item)[0]}</span>{#if menuText(item)[1]}<span class="detail">{menuText(item).slice(1).join(' · ')}</span>{/if}</span>
        {#if !inert(item)}<span class="kind" title={opensPage(item) ? t('Open') : t('Play')}><Icon name={opensPage(item) ? 'folders' : 'play'} size={16} /></span>{/if}
      </button>
    </div>
  {/each}
</div>

<style>
  .search { display: flex; gap: 8px; margin: 0 0 14px; max-width: 520px; }
  .search input { flex: 1; min-width: 0; padding: 8px 10px; border-radius: 8px; border: 1px solid var(--line, var(--panel-strong)); background: var(--panel-strong); color: var(--fg); font: inherit; }
  .search button { padding: 8px 14px; border-radius: 8px; border: 0; background: var(--accent); color: var(--on-accent); font: inherit; cursor: pointer; } .search button:disabled { opacity: 0.5; cursor: default; }
  .items { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 4px 16px; }
  .item { display: flex; align-items: center; border-radius: 8px; } .item:hover { background: var(--hover); }
  .main { flex: 1; min-width: 0; min-height: 44px; display: flex; align-items: center; gap: 10px; background: none; border: 0; color: var(--fg); padding: 6px 8px; cursor: pointer; font: inherit; text-align: left; } .main:disabled { cursor: default; color: var(--fg-quiet); }
  .main img { width: 40px; height: 40px; border-radius: 6px; object-fit: cover; background: var(--panel-strong); flex-shrink: 0; }
  .text { flex: 1; min-width: 0; display: flex; flex-direction: column; } .detail { color: var(--fg-quiet); font-size: 12px; display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 2; overflow: hidden; line-height: 1.3; }
  .name { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .kind { color: var(--fg-quiet); display: grid; place-items: center; opacity: 0.7; } .item:hover .kind { color: var(--accent); opacity: 1; }
  .quiet { color: var(--fg-quiet); }
</style>
