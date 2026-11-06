<script lang="ts">
  import { artUrl } from '../bridge'
  // Up to four covers in a square: 1 fills it, 2 split it, 3 are one large and two small, 4 make a 2×2; none shows the name's first letter
  let { covers = [], name = '', size = 300 }: { covers?: string[]; name?: string; size?: number } = $props()
  const shown = $derived(covers.slice(0, 4))
</script>

<div class="mosaic count-{shown.length}">
  {#each shown as cover, position (cover)}<img class="cell-{position}" src={artUrl(cover, shown.length === 1 || (shown.length === 3 && position === 0) ? size : Math.round(size / 2))} alt="" loading="lazy" />{/each}
  {#if !shown.length}<span class="letter">{(name.trim()[0] ?? '♪').toUpperCase()}</span>{/if}
</div>

<style>
  .mosaic { aspect-ratio: 1; display: grid; gap: 1px; border-radius: 10px; overflow: hidden; background: color-mix(in srgb, var(--fg) 8%, transparent); box-shadow: 0 6px 16px var(--shadow); }
  img { width: 100%; height: 100%; object-fit: cover; display: block; min-height: 0; }
  .count-2 { grid-template-columns: 1fr 1fr; }
  .count-3 { grid-template-columns: 1fr 1fr; grid-template-rows: 1fr 1fr; } .count-3 .cell-0 { grid-row: span 2; }
  .count-4 { grid-template-columns: 1fr 1fr; grid-template-rows: 1fr 1fr; }
  .letter { display: flex; align-items: center; justify-content: center; font-size: 40px; font-weight: 600; color: var(--fg-quiet); }
</style>
