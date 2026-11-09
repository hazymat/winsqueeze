<script lang="ts">
  import type { Snippet } from 'svelte'
  // Every browse page's title bar (SPEC.md › Browsing): big title and a quiet detail on the left, page controls (view switcher, Sort/Group, buttons) on the right; stays pinned while the page scrolls under it
  let { title, detail = '', titleExtra, children }: { title: string; detail?: string; titleExtra?: Snippet; children?: Snippet } = $props()
  let height = $state(0)
  $effect(() => { document.documentElement.style.setProperty('--page-head', `${height}px`) }) // History's day headings stick just below it
</script>

<div class="page-head" bind:clientHeight={height}>
  <div class="titles"><h2 title={title}>{title}</h2>{#if detail}<span class="detail">{detail}</span>{/if}{@render titleExtra?.()}</div>
  {#if children}<div class="controls">{@render children()}</div>{/if}
</div>

<style>
  /* the title keeps at least 240px: longer controls (translated Sort and Group) move onto their own row before the title is cut short */
  .page-head { position: sticky; top: -4px; z-index: 6; display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin: 0 -20px 12px; padding: 8px 20px; min-height: 48px; background: var(--glass); backdrop-filter: blur(var(--blur)) saturate(1.4); border-bottom: 1px solid var(--line); }
  .titles { flex: 1 1 auto; min-width: 0; display: flex; align-items: baseline; gap: 10px; }
  h2 { margin: 0; font-size: 24px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .detail { color: var(--fg-quiet); font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .controls { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; justify-content: flex-end; }
</style>
