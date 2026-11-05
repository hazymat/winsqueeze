<script lang="ts">
  import { command, live, artUrl } from '../bridge'
  import { app, formatTime, toast, newPlaylist } from '../state.svelte'
  let { compact = false }: { compact?: boolean } = $props()
  let dragFrom = $state<number | null>(null)
  const queue = $derived<any[]>(app.nowPlaying?.playlist_loop ?? [])
  const currentIndex = $derived(Number(app.nowPlaying?.playlist_cur_index ?? -1))
  const remaining = $derived(queue.filter(item => Number(item['playlist index']) >= currentIndex).reduce((total, item) => total + Number(item.duration || 0), 0) - Number(app.nowPlaying?.time ?? 0))
  const drop = (toIndex: number) => { if (dragFrom !== null && dragFrom !== toIndex) command('playlist', 'move', dragFrom, toIndex); dragFrom = null }
  async function saveQueue() { const name = prompt('Save queue as Lyrion playlist named:'); if (!name) return; await live(['playlist', 'save', name], true); toast(`Saved queue as “${name}”`) }
  const startPlaylistFromQueue = () => newPlaylist('Untitled playlist', queue.map(item => ({ id: Number(item.id), title: item.title, artist: item.artist ?? '', duration: Number(item.duration || 0) })))
</script>

{#if !compact}<div class="head"><h2>Now playing</h2><span class="quiet">{queue.length} tracks · {formatTime(Math.max(remaining, 0))} remaining</span>
  <button class="ghost" onclick={saveQueue}>Save as playlist</button><button class="ghost" onclick={startPlaylistFromQueue}>Edit as new playlist</button><button class="ghost" onclick={() => command('playlist', 'clear')}>Clear</button></div>{/if}
{#if !queue.length}<p class="quiet">The queue is empty.</p>{/if}
<div class="queue">
  {#each queue as item (item['playlist index'] + ':' + item.id)}
    {@const index = Number(item['playlist index'])}
    <div class="item" class:current={index === currentIndex} draggable="true" role="listitem" ondragstart={() => dragFrom = index} ondragover={(event) => event.preventDefault()} ondrop={() => drop(index)}>
      <button class="main" onclick={() => command('playlist', 'index', index)}>
        {#if !compact}{#if item.coverid}<img src={artUrl(item.coverid, 80)} alt="" loading="lazy" />{:else}<span class="glyph">♪</span>{/if}{/if}
        <span class="text"><span class="title">{item.title}</span><span class="quiet">{item.artist}{item.album ? ` · ${item.album}` : ''}</span></span>
        <span class="quiet">{formatTime(Number(item.duration))}</span>
      </button>
      <button class="remove" onclick={() => command('playlist', 'delete', index)} aria-label="Remove">✕</button>
    </div>
  {/each}
</div>

<style>
  .head { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 10px; }
  h2 { margin: 0; font-size: 24px; }
  .queue { display: flex; flex-direction: column; }
  .item { display: flex; align-items: center; border-radius: 8px; }
  .item:hover { background: var(--hover); }
  .item.current { background: var(--accent-soft); }
  .main { flex: 1; min-width: 0; display: flex; align-items: center; gap: 10px; background: none; border: 0; color: var(--fg); padding: 5px 8px; cursor: pointer; font: inherit; text-align: left; }
  .main img, .glyph { width: 36px; height: 36px; border-radius: 5px; object-fit: cover; display: grid; place-items: center; background: var(--panel-strong); flex-shrink: 0; }
  .text { flex: 1; min-width: 0; display: flex; flex-direction: column; }
  .title, .text .quiet { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .remove { background: none; border: 0; color: var(--fg-quiet); cursor: pointer; padding: 0 10px; opacity: 0; }
  .item:hover .remove { opacity: 1; }
  .quiet { color: var(--fg-quiet); font-size: 12px; }
</style>
