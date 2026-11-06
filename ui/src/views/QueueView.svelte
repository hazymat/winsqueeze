<script lang="ts">
  import Hint from '../parts/Hint.svelte'
  import QueueEndMenu from '../parts/QueueEndMenu.svelte'
  import { playlistUrls, refillPlaylist } from '../lib/browseActions'
  import { command, live, trackArt, request } from '../bridge'
  import { app, ui, formatTime, toast, newPlaylist } from '../state.svelte'
  import { isDamaged } from '../lib/damaged.svelte'
  import { sortable, planMoves, blockOrder } from '../lib/sortable'
  import { isSelected, rowClick, selectOnHold, selection, type SelectionAction } from '../lib/selection.svelte'
  // The player's queue: hold to reorder (several at once when chosen), choose rows to remove or play next
  const scope = 'queue'
  let localQueue = $state<any[] | null>(null)
  let holdLocalUntil = 0
  const serverQueue = $derived<any[]>(app.nowPlaying?.playlist_loop ?? [])
  // Show our own change straight away; the server's poll catches up a moment later
  $effect(() => { serverQueue; if (Date.now() > holdLocalUntil) localQueue = null })
  const queue = $derived(localQueue ?? serverQueue)
  const currentIndex = $derived(Number(app.nowPlaying?.playlist_cur_index ?? -1))
  const remaining = $derived(queue.filter((item, position) => position >= currentIndex).reduce((total, item) => total + Number(item.duration || 0), 0) - Number(app.nowPlaying?.time ?? 0))
  const keys = $derived(queue.map((item, position) => String(position)))
  const selectedPositions = () => selection.scope === scope ? selection.keys.map(Number).sort((first, second) => first - second) : []

  async function applyOrder(finalOrder: number[], label: string, undoable = true) {
    const before = queue.slice()
    localQueue = finalOrder.map(position => before[position]); holdLocalUntil = Date.now() + 4000
    for (const [from, to] of planMoves(before.map((item, position) => position), finalOrder)) await live(['playlist', 'move', from, to], true)
    holdLocalUntil = Date.now() + 1500
    if (undoable) toast(label, () => { const inverse = finalOrder.map((unused, position) => finalOrder.indexOf(position)); applyOrder(inverse, 'Order restored', false) }, 6000, { redo: () => applyOrder(finalOrder, label, false) })
  }
  function onMove(from: number, to: number) {
    const selected = selectedPositions()
    const finalOrder = blockOrder(queue.length, selected.includes(from) ? selected : [from], from, to)
    applyOrder(finalOrder, selected.includes(from) && selected.length > 1 ? `Moved ${selected.length} tracks` : `Moved “${queue[from]?.title}”`)
    if (selected.includes(from)) selection.keys = selected.map(position => String(finalOrder.indexOf(position)))
  }
  async function remove(positionKeys: string[]) {
    const positions = positionKeys.map(Number).sort((first, second) => second - first), removed = positions.map(position => ({ position, item: queue[position] }))
    localQueue = queue.filter((item, position) => !positions.includes(position)); holdLocalUntil = Date.now() + 4000
    for (const position of positions) await live(['playlist', 'delete', position], true)
    holdLocalUntil = Date.now() + 1500
    toast(removed.length === 1 ? `Removed “${removed[0].item?.title}”` : `Removed ${removed.length} tracks`, async () => {
      // Put them back where they were: append each, then move it into place (lowest position first)
      for (const { position, item } of removed.slice().reverse()) { if (Number(item?.id) > 0) { await live(['playlistcontrol', 'cmd:add', `track_id:${item.id}`], true); const status = await live(['status', '-', 1], true); await live(['playlist', 'move', Number(status?.playlist_tracks ?? 1) - 1, position], true) } }
      toast('Tracks restored')
    }, 6000, { redo: async () => { for (const position of positions) await live(['playlist', 'delete', position], true) } })
  }
  function playNext(positionKeys: string[]) {
    const positions = positionKeys.map(Number).sort((first, second) => first - second).filter(position => position !== currentIndex)
    const order = queue.map((item, position) => position).filter(position => !positions.includes(position))
    const after = order.indexOf(currentIndex) + 1
    applyOrder(order.slice(0, after).concat(positions, order.slice(after)), positions.length === 1 ? `“${queue[positions[0]]?.title}” plays next` : `${positions.length} tracks play next`)
  }
  const actions: SelectionAction[] = [
    { label: 'Play now', single: true, key: 'Enter', run: positionKeys => command('playlist', 'index', Number(positionKeys[0])) },
    { label: 'Play next', run: playNext },
    { label: 'New playlist from these', run: positionKeys => newPlaylist('Untitled playlist', positionKeys.map(Number).sort((first, second) => first - second).map(position => queue[position]).map(item => ({ id: Number(item.id), title: item.title, artist: item.artist ?? '', duration: Number(item.duration || 0) }))) },
    { label: 'Track info', run: positionKeys => { ui.trackInfo = positionKeys.map(Number).sort((first, second) => first - second).map(position => Number(queue[position]?.id)).filter(id => id > 0) } },
    { label: 'Remove', key: 'Delete', danger: true, run: remove }
  ]
  // Saving over an existing playlist keeps its old tracks, so Undo can put them back
  async function saveQueue() {
    const name = prompt('Save the queue as a Lyrion playlist named:'); if (!name) return
    const existing = ((await live(['playlists', 0, 1000]))?.playlists_loop ?? []).find((savedList: any) => savedList.playlist === name)
    const oldUrls = existing ? await playlistUrls(String(existing.id)) : null
    const saved = await live(['playlist', 'save', name], true), savedId = String(saved?.__playlist_id ?? existing?.id ?? '')
    toast(existing ? `Replaced “${name}” with the queue` : `Saved queue as “${name}”`, async () => {
      if (!savedId) return
      if (oldUrls) { await refillPlaylist(savedId, oldUrls); toast(`“${name}” is back as it was`) } else { await live(['playlists', 'delete', `playlist_id:${savedId}`]); toast(`Removed “${name}”`) }
    }, 30000, { redo: () => live(['playlist', 'save', name], true) })
  }
  const startPlaylistFromQueue = () => newPlaylist('Untitled playlist', queue.map(item => ({ id: Number(item.id), title: item.title, artist: item.artist ?? '', duration: Number(item.duration || 0) })))
  // Undo brings the queue back as it was: same tracks, same order, same track and spot
  const clearQueue = () => {
    const ids = queue.map(item => Number(item.id)).filter(id => id > 0), index = Math.max(currentIndex, 0), seconds = Number(app.nowPlaying?.time ?? 0), wasPlaying = app.nowPlaying?.mode === 'play'
    command('playlist', 'clear')
    toast('Queue cleared', async () => {
      if (!ids.length) return
      await request({ type: 'playTracks', ids, mode: 'load', start: index })
      if (seconds > 2) await live(['time', seconds], true)
      if (!wasPlaying) await live(['pause', '1'], true)
    }, 6000, { redo: () => command('playlist', 'clear') })
  }
</script>

<div class="head">
  <h3>Up next</h3><span class="quiet">{queue.length} {queue.length === 1 ? 'track' : 'tracks'} · {formatTime(Math.max(remaining, 0))} left</span>
  <div class="tools"><QueueEndMenu /><button class="ghost" onclick={saveQueue}>Save</button><button class="ghost" onclick={startPlaylistFromQueue} title="Edit the queue as a new playlist">Edit</button><button class="ghost" onclick={clearQueue}>Clear</button></div>
</div>
{#if queue.length > 1}<Hint id="queue-reorder" text="Press and hold, then drag to reorder." /><Hint id="queue-multiselect" text="Press and hold without dragging to select several tracks." />{/if}
{#if !queue.length}<p class="quiet">The queue is empty.</p>{/if}
<div class="queue" use:sortable={{ onMove, onHold: position => selectOnHold(scope, String(position), actions, 'track'), group: position => selection.scope === scope && selection.keys.includes(String(position)) ? selection.keys.map(Number) : [position] }}>
  {#each queue as item, position (position + ':' + item.id)}
    <button data-sort-row class="item" class:current={position === currentIndex} class:selected={isSelected(scope, String(position))}
      onclick={(event) => { if (!rowClick(event, scope, String(position), keys, actions, 'track')) command('playlist', 'index', position) }}>
      {#if trackArt(item)}<img src={trackArt(item, 80)} alt="" loading="lazy" draggable="false" />{:else}<span class="glyph">♪</span>{/if}
      <span class="text"><span class="title">{item.title}{#if isDamaged(item.id)}<span class="damaged-mark" title="Damaged file: playback may skip">⚠</span>{/if}</span><span class="quiet">{item.artist}{item.album ? ` · ${item.album}` : ''}</span></span>
      {#if position === currentIndex}<span class="playing" aria-label="Playing">▶</span>{/if}
      <span class="quiet time">{formatTime(Number(item.duration))}</span>
    </button>
  {/each}
</div>

<style>
  .head { display: flex; align-items: center; gap: 4px 10px; flex-wrap: wrap; margin-bottom: 8px; container-type: inline-size; }
  h3 { margin: 0; color: var(--fg); font-size: 16px; }
  .tools { display: flex; align-items: center; gap: 6px; margin-left: auto; }
  .tools .ghost { height: 28px; box-sizing: border-box; padding: 0 11px; font-size: 12px; white-space: nowrap; border-radius: 999px; }
  /* narrow queue panel: the toolbar takes its own row, the pill drops its label */
  @container (max-width: 600px) { .tools { margin-left: 0; width: 100%; justify-content: flex-end; } }
  @container (max-width: 345px) { .tools :global(.queue-end .label) { display: none; } }
  .queue { display: flex; flex-direction: column; gap: 2px; }
  .item { display: flex; align-items: center; gap: 12px; width: 100%; min-height: 52px; background: none; border: 0; border-radius: 10px; color: var(--fg); padding: 6px 10px; cursor: pointer; font: inherit; text-align: left; }
  .item:hover { background: var(--hover); }
  .item.current { background: var(--accent-soft); }
  img, .glyph { width: 40px; height: 40px; border-radius: 6px; object-fit: cover; display: grid; place-items: center; background: var(--panel-strong); flex-shrink: 0; }
  .text { flex: 1; min-width: 0; display: flex; flex-direction: column; }
  .text span { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .title { font-weight: 500; }
  .quiet { color: var(--fg-quiet); font-size: 12px; }
  .playing { color: var(--accent); font-size: 11px; }
  .time { font-variant-numeric: tabular-nums; }
</style>
