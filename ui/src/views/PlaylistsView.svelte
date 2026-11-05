<script lang="ts">
  import { untrack } from 'svelte'
  import { live, artUrl } from '../bridge'
  import { app, ui, openView, toast, formatTime, newPlaylist } from '../state.svelte'
  import { sortable, planMoves, blockOrder } from '../lib/sortable'
  import { selectable } from '../lib/selectable.svelte'
  import { browseActions } from '../lib/browseActions'
  import { isSelected, rowClick, selectOnHold, selection, type SelectionAction } from '../lib/selection.svelte'
  // Lyrion's saved playlists, read live (small, and other controllers change them)
  let { playlistId = '', title = '' }: { playlistId?: string; title?: string } = $props()
  let items = $state<any[]>([])
  let loading = $state(true)
  async function load() {
    loading = true
    const result = playlistId ? await live(['playlists', 'tracks', 0, 1000, `playlist_id:${playlistId}`, 'tags:adlcu']) : await live(['playlists', 0, 1000, 'tags:u'])
    items = (playlistId ? result?.playlisttracks_loop : result?.playlists_loop) ?? []
    loading = false
  }
  const serverName = $derived(String(app.status?.server ?? ''))
  $effect(() => { playlistId; serverName; untrack(load) })
  const plain = (text: string) => (text ?? '').toLowerCase()
  const shown = $derived(ui.search.length >= 2 ? items.filter(item => plain(item.playlist ?? item.title).includes(plain(ui.search))) : items)
  const totalSeconds = $derived(items.reduce((total, item) => total + Number(item.duration || 0), 0))
  async function play(id: string, name: string, start = 0) { await live(['playlistcontrol', 'cmd:load', `playlist_id:${id}`], true); if (start) await live(['playlist', 'index', start], true); toast(`Playing ${name}`) }
  // Editing a saved playlist on the server: reorder and remove, each with Undo
  const scope = 'playlist'
  const keys = $derived(items.map((item, index) => String(index)))
  async function applyOrder(finalOrder: number[], label: string, undoable = true) {
    const before = items.slice(); items = finalOrder.map(index => before[index])
    for (const [from, to] of planMoves(before.map((item, index) => index), finalOrder)) await live(['playlists', 'edit', 'cmd:move', `playlist_id:${playlistId}`, `index:${from}`, `toindex:${to}`])
    if (undoable) toast(label, () => applyOrder(finalOrder.map((unused, index) => finalOrder.indexOf(index)), 'Order restored', false))
  }
  function onMove(from: number, to: number) {
    const selected = selection.scope === scope ? selection.keys.map(Number) : []
    const order = blockOrder(items.length, selected.includes(from) ? selected : [from], from, to)
    applyOrder(order, `Moved in ${title}`)
    if (selected.includes(from)) selection.keys = selected.map(index => String(order.indexOf(index)))
  }
  async function remove(indexKeys: string[]) {
    const indexes = indexKeys.map(Number).sort((first, second) => second - first), removed = indexes.map(index => ({ index, item: items[index] }))
    items = items.filter((item, index) => !indexes.includes(index))
    for (const index of indexes) await live(['playlists', 'edit', 'cmd:delete', `playlist_id:${playlistId}`, `index:${index}`])
    toast(removed.length === 1 ? `Removed “${removed[0].item?.title}” from ${title}` : `Removed ${removed.length} tracks from ${title}`, async () => {
      for (const { index, item } of removed.slice().reverse()) { if (!item?.url) continue; await live(['playlists', 'edit', 'cmd:add', `playlist_id:${playlistId}`, `url:${item.url}`]); const count = items.length + 1; await live(['playlists', 'edit', 'cmd:move', `playlist_id:${playlistId}`, `index:${count - 1}`, `toindex:${index}`]) ; items = items.slice(0, index).concat([item], items.slice(index)) }
    })
  }
  const actions: SelectionAction[] = [
    { label: 'Play from here', single: true, key: 'Enter', run: indexKeys => play(playlistId, title, Number(indexKeys[0])) },
    { label: 'Remove from playlist', key: 'Delete', danger: true, run: remove }
  ]
  const edit = () => newPlaylist(title, items.map(item => ({ id: Number(item.id), title: item.title, artist: item.artist ?? '', duration: Number(item.duration || 0) })))
</script>

{#if playlistId}
  <div class="head"><h2>{title}</h2><span class="quiet">{items.length} tracks · {formatTime(totalSeconds)}</span>
    <button class="primary" onclick={() => play(playlistId, title)}>Play</button><button class="ghost" onclick={edit}>Edit as new playlist</button></div>
  <div class="list" use:sortable={{ onMove, onHold: index => selectOnHold(scope, String(index), actions, 'track'), group: index => selection.scope === scope && selection.keys.includes(String(index)) ? selection.keys.map(Number) : [index], enabled: ui.search.length < 2 }}>
  {#each shown as item, index (index + ':' + item.id)}
    <button data-sort-row class="row" class:selected={isSelected(scope, String(index))} onclick={(event) => { if (!rowClick(event, scope, String(index), keys, actions, 'track')) play(playlistId, title, index) }}>
      {#if item.coverid}<img src={artUrl(item.coverid, 80)} alt="" loading="lazy" />{:else}<span class="glyph">♪</span>{/if}
      <span class="text"><span>{item.title}</span><span class="quiet">{item.artist}{item.album ? ` · ${item.album}` : ''}</span></span>
      <span class="quiet">{formatTime(Number(item.duration))}</span>
    </button>
  {/each}
  </div>
{:else}
  <h2>Playlists</h2>
  {#if !loading && !items.length}<p class="quiet">No saved playlists on the server yet. Use New playlist to make one.</p>{/if}
  <div use:selectable={{ scope: 'playlists', noun: 'playlist', actions: browseActions('playlist') }}>
  {#each shown as item (item.id)}
    <div class="row" data-select-key={String(item.id)}>
      <button class="main" onclick={() => openView({ kind: 'playlist', key: String(item.id), title: item.playlist })}><span class="glyph">☰</span><span class="text"><span>{item.playlist}</span></span></button>
      <button class="ghost" onclick={() => play(String(item.id), item.playlist)}>Play</button>
    </div>
  {/each}
  </div>
{/if}

<style>
  .head { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 10px; }
  .head h2 { margin: 0; }
  .list { display: flex; flex-direction: column; gap: 2px; }
  .row { display: flex; align-items: center; gap: 10px; width: 100%; min-height: 48px; background: none; border: 0; color: var(--fg); padding: 5px 8px; border-radius: 8px; font: inherit; text-align: left; cursor: pointer; }
  .row:hover { background: var(--hover); }
  .main { flex: 1; min-width: 0; display: flex; align-items: center; gap: 10px; background: none; border: 0; color: var(--fg); font: inherit; text-align: left; cursor: pointer; padding: 0; }
  img, .glyph { width: 36px; height: 36px; border-radius: 5px; object-fit: cover; display: grid; place-items: center; background: var(--panel-strong); flex-shrink: 0; }
  .text { flex: 1; min-width: 0; display: flex; flex-direction: column; }
  .text span { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
</style>
