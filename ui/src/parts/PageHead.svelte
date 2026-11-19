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
  .page-head { position: sticky; top: -4px; z-index: 6; display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin: 0 -20px 12px; padding: 8px 20px; min-height: 48px; border-bottom: 1px solid var(--line); box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.06); }
  /* frosted glass with grain and a light top edge. The frost layer reaches 64px below the head and is clipped back: on high-DPI screens Chromium leaves the strip within ~2 blur radii of a backdrop's bottom edge unblurred, so scrolled rows showed through sharp */
  .page-head::before { content: ""; position: absolute; inset: 0 0 -64px 0; z-index: -1; pointer-events: none; clip-path: inset(0 0 64px 0); background-color: var(--head-glass, var(--glass)); background-image: linear-gradient(180deg, rgba(255, 255, 255, 0.08), rgba(255, 255, 255, 0.02)), url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='160' height='160'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='.85' numOctaves='3' stitchTiles='stitch'/%3E%3CfeColorMatrix values='0 0 0 0 1 0 0 0 0 1 0 0 0 0 1 0 0 0 .06 0'/%3E%3C/filter%3E%3Crect width='160' height='160' filter='url(%23n)'/%3E%3C/svg%3E"); background-size: 100% calc(100% - 64px), 160px 160px; background-repeat: no-repeat, repeat; backdrop-filter: blur(min(var(--blur), 14px)) saturate(1.8); }
  .titles { flex: 1 1 auto; min-width: 0; display: flex; align-items: baseline; gap: 10px; }
  h2 { margin: 0; font-size: 24px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .detail { color: var(--fg-quiet); font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .controls { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; justify-content: flex-end; }
</style>
