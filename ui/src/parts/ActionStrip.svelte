<script lang="ts">
  import { ui, undoToast } from '../state.svelte'
  import { selection, clearSelection, runAction } from '../lib/selection.svelte'
  // One fixed place in the layout, above the transport, for the selection's actions and for messages with Undo
  const count = $derived(selection.keys.length)
  const actions = $derived(selection.actions.filter(action => !(action.single && count > 1)))
  const visible = $derived(count > 0 || !!ui.toast)
</script>

<div class="strip" class:visible role="status" aria-live="polite">
  {#if count}
    <span class="count">{count} {selection.noun}{count === 1 ? '' : 's'} selected</span>
    <div class="actions">
      {#each actions as action (action.label)}<button class:danger={action.danger} onclick={() => runAction(action)}>{action.label}{#if action.key}<kbd>{action.key === 'Delete' ? 'Del' : action.key}</kbd>{/if}</button>{/each}
      <button class="clear" onclick={clearSelection} aria-label="Clear selection">Done<kbd>Esc</kbd></button>
    </div>
  {:else if ui.toast}
    {#key ui.toast.id}
      <svg class="ring" viewBox="0 0 20 20" aria-hidden="true" style:--ms={`${ui.toast.ms}ms`}><circle class="track" cx="10" cy="10" r="8" /><circle class="left" cx="10" cy="10" r="8" pathLength="100" /></svg>
    {/key}
    <span class="message">{ui.toast.text}</span>
    {#if ui.toast.undo}<button class="undo" onclick={undoToast}>Undo<kbd>Ctrl+Z</kbd></button>{/if}
  {/if}
</div>

<style>
  .strip { display: flex; align-items: center; gap: 12px; height: 0; overflow: hidden; padding: 0 16px; background: var(--glass); backdrop-filter: blur(var(--blur)) saturate(1.4); border-top: 1px solid transparent; font-size: 13px; transition: height 160ms ease, border-color 160ms; }
  .strip.visible { height: 44px; border-top-color: var(--line); }
  .count { font-weight: 600; white-space: nowrap; }
  .actions { display: flex; gap: 6px; margin-left: auto; overflow-x: auto; scrollbar-width: none; }
  button { display: flex; align-items: center; gap: 6px; white-space: nowrap; background: var(--panel); color: var(--fg); border: 1px solid var(--line); border-radius: 8px; padding: 5px 12px; font: inherit; cursor: pointer; min-height: 32px; }
  button:hover { background: var(--hover); }
  button.danger { color: #ff7a6b; }
  .clear { background: none; }
  .undo { margin-left: auto; color: var(--accent); font-weight: 600; background: none; }
  kbd { font-family: var(--font-ui); font-size: 10px; color: var(--fg-quiet); border: 1px solid var(--line); border-radius: 4px; padding: 0 4px; }
  .message { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .ring { flex: none; width: 18px; height: 18px; transform: rotate(-90deg); }
  .ring circle { fill: none; stroke-width: 2.6; }
  .ring .track { stroke: var(--track); }
  .ring .left { stroke: var(--accent); stroke-linecap: round; stroke-dasharray: 100; animation: run-down var(--ms, 6s) linear forwards; }
  @keyframes run-down { to { stroke-dashoffset: -100; } }
  @media (max-width: 540px) { kbd { display: none; } }
</style>
