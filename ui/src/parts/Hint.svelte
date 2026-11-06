<script lang="ts">
  import { app } from '../state.svelte'
  import { sendToShell } from '../bridge'
  // A dismissable tip: gone for good once closed; Settings › General can hide them all or bring them back
  let { id, text }: { id: string; text: string } = $props()
  const shown = $derived(!app.hints.hidden && !app.hints.dismissed.includes(id))
  function dismiss() { app.hints = { hidden: app.hints.hidden, dismissed: app.hints.dismissed.concat([id]) }; sendToShell({ type: 'dismissHint', id }) }
</script>

{#if shown}
  <div class="hint" role="note"><span class="bulb" aria-hidden="true">i</span><span class="text">{text}</span><button class="close" onclick={dismiss} aria-label="Dismiss hint" title="Got it">✕</button></div>
{/if}

<style>
  .hint { display: flex; align-items: center; gap: 8px; margin: 0 0 6px; padding: 6px 6px 6px 8px; border-radius: 8px; background: var(--accent-soft); color: var(--fg); font-size: 12px; line-height: 1.35; }
  .bulb { display: grid; place-items: center; flex: none; width: 16px; height: 16px; border-radius: 50%; background: var(--accent); color: var(--on-accent); font-size: 10px; font-weight: 700; font-style: italic; font-family: Georgia, serif; }
  .text { flex: 1; min-width: 0; }
  .close { flex: none; width: 22px; height: 22px; padding: 0; background: none; border: 0; border-radius: 6px; color: var(--fg-quiet); cursor: pointer; font-size: 11px; }
  .close:hover { background: var(--hover); color: var(--fg); }
</style>
