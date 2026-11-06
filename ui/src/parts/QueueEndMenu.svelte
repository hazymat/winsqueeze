<script lang="ts">
  import { app } from '../state.svelte'
  import { queueEndMode, setQueueEnd, setPlayback, type QueueEnd } from '../lib/queueEnd.svelte'
  // "When the queue ends" in Up next: stop, repeat all or keep playing, and what Keep playing adds
  let open = $state(false)
  let root: HTMLElement, menu = $state<HTMLElement>()
  let place = $state({ top: 0, right: 0 })
  // The menu lives on <body> so the queue panel's scrolling doesn't clip it
  const portal = (node: HTMLElement) => { document.body.appendChild(node); return { destroy: () => node.remove() } }
  function toggle() { const rect = root.getBoundingClientRect(); place = { top: rect.bottom + 6, right: window.innerWidth - rect.right }; open = !open }
  const mode = $derived(queueEndMode())
  const modes: [QueueEnd, string, string][] = [['stop', 'Stop', 'Stop'], ['repeat', 'Repeat all', 'Repeat all'], ['continue', 'Keep playing', 'Keep playing from the library']]
  const adds: [string, string][] = [['album', 'A whole album'], ['tracks', 'About ten tracks']]
  const looks: [string, string][] = [['artist', 'Same artist, then genre'], ['genre', 'Same genre'], ['anything', 'Anything in the library']]
</script>

<svelte:window onmousedown={(event) => { if (open && !root.contains(event.target as Node) && !menu?.contains(event.target as Node)) open = false }} onkeydown={(event) => { if (open && event.key === 'Escape') { event.stopPropagation(); open = false } }} />

<div class="queue-end" bind:this={root}>
  <button class="current" class:open class:active={mode !== 'stop'} onclick={toggle} aria-haspopup="menu" aria-expanded={open} title="What happens when the queue ends">
    <span class="label">At the end</span><span class="value">{modes.find(([value]) => value === mode)?.[1]}</span><svg class="chevron" viewBox="0 0 10 6" width="9" height="6" aria-hidden="true"><path d="M1 1l4 4 4-4" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" /></svg>
  </button>
  {#if open}
    <div class="queue-end-menu" role="menu" bind:this={menu} use:portal style:top="{place.top}px" style:right="{place.right}px" style:max-height="calc(100vh - {place.top + 12}px)">
      <div class="menu-title">When the queue ends</div>
      {#each modes as [value, unused, label] (value)}<button role="menuitemradio" aria-checked={mode === value} class:on={mode === value} onclick={() => { setQueueEnd(value); if (value !== 'continue') open = false }}><span>{label}</span>{#if mode === value}<span class="tick">✓</span>{/if}</button>{/each}
      {#if mode === 'continue'}
        <div class="menu-title">Add</div>
        {#each adds as [value, label] (value)}<button role="menuitemradio" aria-checked={app.playback.ContinueAdds === value} class:on={app.playback.ContinueAdds === value} onclick={() => setPlayback({ ContinueAdds: value })}><span>{label}</span>{#if app.playback.ContinueAdds === value}<span class="tick">✓</span>{/if}</button>{/each}
        <div class="menu-title">Look for</div>
        {#each looks as [value, label] (value)}<button role="menuitemradio" aria-checked={app.playback.ContinueFrom === value} class:on={app.playback.ContinueFrom === value} onclick={() => setPlayback({ ContinueFrom: value })}><span>{label}</span>{#if app.playback.ContinueFrom === value}<span class="tick">✓</span>{/if}</button>{/each}
        <p class="note">Adds music just before the last track ends, skipping anything it added in the last three days. Each top-up can be undone.</p>
      {/if}
    </div>
  {/if}
</div>

<style>
  .queue-end { position: relative; }
  .current { display: flex; flex-wrap: wrap; align-content: center; align-items: baseline; gap: 7px; height: 28px; padding: 0 10px 0 11px; background: var(--panel); color: var(--fg); border: 1px solid var(--line); border-radius: 999px; font: inherit; font-size: 12px; cursor: pointer; white-space: nowrap; }
  .current:hover, .current.open { background: var(--hover); }
  .current.active .value { color: var(--accent); }
  .label, .value { line-height: 1; } .label { color: var(--fg-quiet); font-size: 10.5px; text-transform: uppercase; letter-spacing: 0.06em; font-weight: 600; }
  .value { font-weight: 600; }
  .chevron { align-self: center; color: var(--fg-quiet); transition: transform 150ms; } .open .chevron { transform: rotate(180deg); }
  .queue-end-menu { position: fixed; width: 250px; overflow-y: auto; z-index: 100; padding: 6px; background: var(--panel-solid); backdrop-filter: blur(24px) saturate(1.5); border: 1px solid var(--line); border-radius: 12px; box-shadow: 0 16px 40px var(--shadow); }
  .queue-end-menu .menu-title { padding: 8px 10px 6px; color: var(--fg-quiet); font-size: 10.5px; text-transform: uppercase; letter-spacing: 0.06em; font-weight: 600; }
  .queue-end-menu button { display: flex; align-items: center; justify-content: space-between; gap: 12px; width: 100%; background: none; border: 0; color: var(--fg); font: inherit; font-size: 13px; text-align: left; padding: 7px 10px; border-radius: 8px; cursor: pointer; }
  .queue-end-menu button:hover { background: var(--hover); }
  .queue-end-menu button.on { background: var(--accent-soft); }
  .queue-end-menu .tick { color: var(--accent); font-weight: 700; }
  .queue-end-menu .note { margin: 6px 10px 4px; color: var(--fg-quiet); font-size: 11.5px; line-height: 1.4; }
</style>
