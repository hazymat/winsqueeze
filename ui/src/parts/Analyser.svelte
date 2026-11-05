<script lang="ts">
  // Hi-fi spectrum analyser window, in the style of the EQ panel: recessed smoked glass, cyan segment columns, gold legends.
  // levels: 0..1 per column; with levelsRight each column splits into a left and a right bar (L/R display)
  let { levels, levelsRight = null, labels = [], segments = 10, scale = [], compact = false }: { levels: number[]; levelsRight?: number[] | null; labels?: string[]; segments?: number; scale?: string[]; compact?: boolean } = $props()
  const segmentIndexes = $derived(Array.from({ length: segments }, (unused, index) => segments - 1 - index))
  const lit = (level: number, segment: number) => level * segments > segment + 0.15
</script>

<div class="analyser" class:compact>
  <div class="window">
    {#if scale.length}<div class="scale">{#each scale as mark}<span>{mark}</span>{/each}</div>{/if}
    <div class="columns">
      {#each levels as level, column}
        <div class="column">
          {#each levelsRight ? [level, levelsRight[column] ?? 0] : [level] as barLevel}
            <div class="bar">{#each segmentIndexes as segment}<i class:on={lit(barLevel, segment)}></i>{/each}</div>
          {/each}
        </div>
      {/each}
    </div>
    {#if scale.length}<div class="scale">{#each scale as mark}<span>{mark}</span>{/each}</div>{/if}
  </div>
  {#if labels.length}<div class="legend" class:offset={scale.length}>{#each labels as label}<span>{label}</span>{/each}</div>{/if}
</div>

<style>
  .analyser { --segment-on: #5fe6ee; --segment-glow: rgba(95, 230, 238, 0.55); --segment-off: rgba(95, 230, 238, 0.09); --legend: #c9a85c; display: flex; flex-direction: column; gap: 4px; }
  .window { display: flex; gap: 6px; flex: 1; min-height: 0; padding: 8px 10px; border-radius: 6px; background: linear-gradient(180deg, rgba(4, 10, 14, 0.55), rgba(10, 18, 24, 0.35)); backdrop-filter: blur(10px) saturate(1.3); box-shadow: inset 0 2px 6px rgba(0, 0, 0, 0.55), inset 0 -1px 0 rgba(255, 255, 255, 0.07), 0 1px 0 rgba(255, 255, 255, 0.06); position: relative; overflow: hidden; }
  /* A faint reflection across the glass */
  .window::after { content: ""; position: absolute; inset: 0; background: linear-gradient(160deg, rgba(255, 255, 255, 0.09) 0%, rgba(255, 255, 255, 0.02) 38%, transparent 40%); pointer-events: none; }
  .columns { flex: 1; display: flex; gap: 3px; min-width: 0; }
  .column { flex: 1; display: flex; gap: 1px; min-width: 0; }
  .bar { flex: 1; display: flex; flex-direction: column; gap: 2px; }
  .bar i { flex: 1; border-radius: 1px; background: var(--segment-off); }
  .bar i.on { background: var(--segment-on); box-shadow: 0 0 4px var(--segment-glow); }
  .scale { display: flex; flex-direction: column; justify-content: space-between; color: var(--legend); font-size: 8px; line-height: 1; font-variant-numeric: tabular-nums; text-align: right; opacity: 0.85; }
  .legend { display: flex; padding: 0 10px; color: var(--legend); font-size: 8px; }
  .legend.offset { padding: 0 32px; } /* lines up with the columns, inside the scales */
  .legend span { flex: 1; min-width: 0; text-align: center; white-space: nowrap; overflow: visible; }
  .compact .window { padding: 3px 4px; border-radius: 4px; gap: 0; }
  .compact .columns { gap: 2px; } .compact .bar { gap: 1px; }
  .compact .bar i.on { box-shadow: 0 0 2px var(--segment-glow); }
</style>
