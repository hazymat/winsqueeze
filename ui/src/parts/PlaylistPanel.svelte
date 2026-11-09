<script lang="ts">
  import { t, tn } from '../lib/i18n'
  import { request, sendToShell, onShellMessage } from '../bridge'
  import { playlist, saveDraft, undoLastAdd, toast, formatTime } from '../state.svelte'
  import { sortable, blockOrder } from '../lib/sortable'
  import { isSelected, rowClick, selectOnHold, selection, type SelectionAction } from '../lib/selection.svelte'
  // Playlist builder (SPEC.md › Playlist builder): docked panel, collapses to a chip, saves to Lyrion
  const scope = 'builder'
  let asFavourite = $state(true)
  let saving = $state(false)
  const totalSeconds = $derived(playlist.items.reduce((total, item) => total + Number(item.duration || 0), 0))
  const keys = $derived(playlist.items.map((item, index) => String(index)))
  function setItems(items: typeof playlist.items, label: string) { const before = playlist.items; playlist.items = items; playlist.history = []; saveDraft(); toast(label, () => { playlist.items = before; saveDraft() }, 6000, { redo: () => { playlist.items = items; saveDraft() } }) }
  function onMove(from: number, to: number) {
    const selected = selection.scope === scope ? selection.keys.map(Number) : []
    const order = blockOrder(playlist.items.length, selected.includes(from) ? selected : [from], from, to)
    setItems(order.map(index => playlist.items[index]), 'Moved')
    if (selected.includes(from)) selection.keys = selected.map(index => String(order.indexOf(index)))
  }
  const remove = (indexKeys: string[]) => { const indexes = indexKeys.map(Number); setItems(playlist.items.filter((item, index) => !indexes.includes(index)), indexes.length === 1 ? t('Removed “{title}”', { title: playlist.items[indexes[0]]?.title }) : t('Removed {count} tracks', { count: indexes.length })) }
  const actions: SelectionAction[] = [{ label: t('Remove'), key: 'Delete', danger: true, run: remove }]
  const preview = () => sendToShell({ type: 'playTracks', ids: playlist.items.map(item => item.id), mode: 'load', start: 0 })
  function close() { playlist.open = false; playlist.items = []; playlist.history = []; saveDraft() }
  let saveProgress = $state({ done: 0, total: 0 })
  onShellMessage('saveProgress', message => saveProgress = { done: message.done, total: message.total })
  async function save() {
    if (!playlist.items.length || !playlist.name.trim()) return
    saving = true; saveProgress = { done: 0, total: 0 }
    const result = await request({ type: 'savePlaylist', name: playlist.name.trim(), ids: playlist.items.map(item => item.id), favourite: asFavourite })
    saving = false
    if (result?.error) { toast(t('Couldn’t save: {error}', { error: result.error })); return }
    toast(asFavourite ? t('Saved “{name}” and added it to favourites', { name: playlist.name }) : t('Saved “{name}”', { name: playlist.name }))
    close()
  }
  const discard = () => { if (!playlist.items.length || confirm(`Discard “${playlist.name}”?`)) close() }
</script>

{#if playlist.open}
  {#if playlist.collapsed}
    <button class="chip" onclick={() => playlist.collapsed = false} title={t('Show the playlist you\'re building')}>☰ {playlist.name} · {playlist.items.length}</button>
  {:else}
    <aside class="panel">
      <div class="head">
        <input class="name" bind:value={playlist.name} oninput={saveDraft} aria-label={t('Playlist name')} />
        <button class="icon" onclick={() => playlist.collapsed = true} title={t('Collapse')} aria-label={t('Collapse')}>⌄</button>
      </div>
      <div class="quiet summary">{tn(playlist.items.length, '{count} track', '{count} tracks')} · {formatTime(totalSeconds)}{#if playlist.history.length}<button class="link" onclick={undoLastAdd}>{t('Undo last add')}</button>{/if}</div>
      <div class="items" use:sortable={{ onMove, onHold: index => selectOnHold(scope, String(index), actions, 'track'), group: index => selection.scope === scope && selection.keys.includes(String(index)) ? selection.keys.map(Number) : [index] }}>
        {#if !playlist.items.length}<p class="quiet">{t('Use ＋ on albums and tracks to add them here.')}</p>{/if}
        {#each playlist.items as item, index (index + ':' + item.id)}
          <div data-sort-row class="item" class:selected={isSelected(scope, String(index))} role="listitem" onclick={(event) => rowClick(event, scope, String(index), keys, actions, 'track')}>
            <span class="text"><span class="title">{item.title}</span><span class="quiet">{item.artist}</span></span>
            <span class="quiet">{formatTime(item.duration)}</span>
            <button class="icon no-hold" onclick={(event) => { event.stopPropagation(); remove([String(index)]) }} aria-label={t('Remove')}>✕</button>
          </div>
        {/each}
      </div>
      <label class="favourite"><input type="checkbox" bind:checked={asFavourite} /> {t('Save as favourite')}</label>
      <div class="actions">
        <button class="ghost" onclick={preview} disabled={!playlist.items.length}>{t('Preview')}</button>
        <button class="ghost" onclick={discard}>{t('Discard')}</button>
        <button class="primary" onclick={save} disabled={saving || !playlist.items.length}>{saving ? (saveProgress.total ? t('Saving {done} of {total}…', { done: saveProgress.done, total: saveProgress.total }) : t('Saving…')) : t('Save & close')}</button>
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
  .item { display: flex; align-items: center; gap: 6px; padding: 6px; min-height: 40px; box-sizing: border-box; border-radius: 8px; cursor: default; }
  .item:hover { background: var(--hover); }
  .text { flex: 1; min-width: 0; display: flex; flex-direction: column; font-size: 13px; }
  .text span { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .quiet { color: var(--fg-quiet); font-size: 12px; }
  .icon { background: none; border: 0; color: var(--fg-quiet); cursor: pointer; border-radius: 4px; padding: 2px 6px; }
  .icon:hover { background: var(--hover); color: var(--fg); }
  .favourite { font-size: 13px; display: flex; gap: 6px; align-items: center; }
  .actions { display: flex; flex-wrap: wrap; gap: 6px; justify-content: flex-end; } .actions button { white-space: nowrap; } /* a longer Save & close moves to its own row instead of breaking up */
  .chip { position: fixed; right: 16px; bottom: 96px; z-index: 20; background: var(--accent); color: var(--on-accent); border: 0; border-radius: 18px; padding: 8px 14px; cursor: pointer; font: inherit; box-shadow: 0 6px 20px var(--shadow); }
  /* Vertical mode: a bottom sheet over the content */
  @media (max-width: 900px) { .panel { position: fixed; left: 0; right: 0; bottom: 0; width: auto; max-height: 60vh; z-index: 30; border-left: 0; border-top: 1px solid var(--line); border-radius: 14px 14px 0 0; background: var(--panel-solid); } }
</style>
