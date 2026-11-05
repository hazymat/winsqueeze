<script lang="ts">
  import { request, sendToShell } from '../bridge'
  import { playlist, saveDraft, undoLastAdd, toast, formatTime } from '../state.svelte'
  // Playlist builder (SPEC.md › Playlist builder): docked panel, collapses to a chip, saves to Lyrion
  let dragFrom = $state<number | null>(null)
  let asFavourite = $state(true)
  let saving = $state(false)
  const totalSeconds = $derived(playlist.items.reduce((total, item) => total + Number(item.duration || 0), 0))
  const drop = (toIndex: number) => { if (dragFrom === null || dragFrom === toIndex) return; const items = playlist.items.slice(); const moved = items.splice(dragFrom, 1)[0]; items.splice(toIndex, 0, moved); playlist.items = items; dragFrom = null; saveDraft() }
  const remove = (index: number) => { playlist.items = playlist.items.filter((item, itemIndex) => itemIndex !== index); playlist.history = []; saveDraft() }
  const preview = () => sendToShell({ type: 'playTracks', ids: playlist.items.map(item => item.id), mode: 'load', start: 0 })
  function close() { playlist.open = false; playlist.items = []; playlist.history = []; saveDraft() }
  async function save() {
    if (!playlist.items.length || !playlist.name.trim()) return
    saving = true
    const result = await request({ type: 'savePlaylist', name: playlist.name.trim(), ids: playlist.items.map(item => item.id), favourite: asFavourite })
    saving = false
    if (result?.error) { toast(`Couldn't save: ${result.error}`); return }
    toast(`Saved “${playlist.name}”${asFavourite ? ' and added it to favourites' : ''}`)
    close()
  }
  const discard = () => { if (!playlist.items.length || confirm(`Discard “${playlist.name}”?`)) close() }
</script>

{#if playlist.open}
  {#if playlist.collapsed}
    <button class="chip" onclick={() => playlist.collapsed = false} title="Show the playlist you're building">☰ {playlist.name} · {playlist.items.length}</button>
  {:else}
    <aside class="panel">
      <div class="head">
        <input class="name" bind:value={playlist.name} oninput={saveDraft} aria-label="Playlist name" />
        <button class="icon" onclick={() => playlist.collapsed = true} title="Collapse" aria-label="Collapse">⌄</button>
      </div>
      <div class="quiet summary">{playlist.items.length} tracks · {formatTime(totalSeconds)}{#if playlist.history.length}<button class="link" onclick={undoLastAdd}>Undo last add</button>{/if}</div>
      <div class="items">
        {#if !playlist.items.length}<p class="quiet">Use ＋ on albums and tracks to add them here.</p>{/if}
        {#each playlist.items as item, index (index + ':' + item.id)}
          <div class="item" draggable="true" role="listitem" ondragstart={() => dragFrom = index} ondragover={(event) => event.preventDefault()} ondrop={() => drop(index)}>
            <span class="grip">⋮⋮</span>
            <span class="text"><span class="title">{item.title}</span><span class="quiet">{item.artist}</span></span>
            <span class="quiet">{formatTime(item.duration)}</span>
            <button class="icon" onclick={() => remove(index)} aria-label="Remove">✕</button>
          </div>
        {/each}
      </div>
      <label class="favourite"><input type="checkbox" bind:checked={asFavourite} /> Save as favourite</label>
      <div class="actions">
        <button class="ghost" onclick={preview} disabled={!playlist.items.length}>Preview</button>
        <button class="ghost" onclick={discard}>Discard</button>
        <button class="primary" onclick={save} disabled={saving || !playlist.items.length}>{saving ? 'Saving…' : 'Save & close'}</button>
      </div>
    </aside>
  {/if}
{/if}

<style>
  .panel { width: 300px; display: flex; flex-direction: column; gap: 8px; padding: 12px; background: var(--glass); backdrop-filter: blur(var(--blur)) saturate(1.4); border-left: 1px solid var(--line); min-height: 0; box-sizing: border-box; }
  .head { display: flex; gap: 6px; }
  .name { flex: 1; min-width: 0; background: var(--panel); color: var(--fg); border: 1px solid var(--line); border-radius: 6px; padding: 6px 8px; font: inherit; font-weight: 600; }
  .summary { font-size: 12px; display: flex; gap: 10px; }
  .link { background: none; border: 0; color: var(--accent); cursor: pointer; padding: 0; font: inherit; }
  .items { flex: 1; overflow-y: auto; min-height: 0; }
  .item { display: flex; align-items: center; gap: 6px; padding: 4px; border-radius: 6px; cursor: grab; }
  .item:hover { background: var(--hover); }
  .grip { color: var(--fg-quiet); font-size: 10px; }
  .text { flex: 1; min-width: 0; display: flex; flex-direction: column; font-size: 13px; }
  .text span { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .quiet { color: var(--fg-quiet); font-size: 12px; }
  .icon { background: none; border: 0; color: var(--fg-quiet); cursor: pointer; border-radius: 4px; padding: 2px 6px; }
  .icon:hover { background: var(--hover); color: var(--fg); }
  .favourite { font-size: 13px; display: flex; gap: 6px; align-items: center; }
  .actions { display: flex; gap: 6px; justify-content: flex-end; }
  .chip { position: fixed; right: 16px; bottom: 96px; z-index: 20; background: var(--accent); color: var(--on-accent); border: 0; border-radius: 18px; padding: 8px 14px; cursor: pointer; font: inherit; box-shadow: 0 6px 20px var(--shadow); }
  /* Vertical mode: a bottom sheet over the content */
  @media (max-width: 900px) { .panel { position: fixed; left: 0; right: 0; bottom: 0; width: auto; max-height: 60vh; z-index: 30; border-left: 0; border-top: 1px solid var(--line); border-radius: 14px 14px 0 0; background: var(--panel-solid); } }
</style>
