<script lang="ts">
  // Hi-fi spectrum analyser, after the classic EQ front panels: a framed smoked-glass window with a title strip, narrow columns of
  // thin segments (faint grey when off, soft teal when lit), a dotted scale each side and an optional FULL RANGE column.
  // levels: 0..1 per column; with levelsRight each column splits into a left and a right bar (L/R display)
  let { levels, levelsRight = null, labels = [], segments = 30, scale = [], compact = false, title = '', fullRange = null }: { levels: number[]; levelsRight?: number[] | null; labels?: string[]; segments?: number; scale?: string[]; compact?: boolean; title?: string; fullRange?: number | null } = $props()
  const segmentIndexes = $derived(Array.from({ length: segments }, (unused, index) => segments - 1 - index))
  const lit = (level: number, segment: number) => level * segments > segment + 0.3
  // fullRange: 0..1 broadband level for the separate FULL RANGE column (null = no column)
</script>

{#snippet column(columnLevels: number[])}
  <div class="column">{#each columnLevels as barLevel}<div class="bar">{#each segmentIndexes as segment}<i class:on={lit(barLevel, segment)}></i>{/each}</div>{/each}</div>
{/snippet}

{#snippet scaleColumn(side: string)}
  <div class="scale {side}">{#each scale as mark}<span><b>{mark}</b><em></em></span>{/each}</div>
{/snippet}

<div class="analyser" class:compact class:labelled={labels.length > 0}>
  <div class="frame">
    {#if title}<div class="title-strip">{title}</div>{/if}
    <div class="window">
      {#if scale.length}{@render scaleColumn('left')}{/if}
      <div class="plot">
        <div class="columns">
          {#each levels as level, index}{@render column(levelsRight ? [level, levelsRight[index] ?? 0] : [level])}{/each}
          {#if fullRange !== null}<div class="full-gap"></div>{@render column([fullRange])}{/if}
        </div>
        {#if labels.length}
          <div class="legend"><div class="legend-bands">{#each labels as label}<span>{label}</span>{/each}</div>{#if fullRange !== null}<div class="full-gap"></div><span class="full-label"></span>{/if}</div>
          <div class="legend-captions"><span class="bracket">FREQUENCY (Hz)</span>{#if fullRange !== null}<div class="full-gap"></div><span class="full-label full-caption"><span>FULL RANGE</span></span>{/if}</div>
        {/if}
      </div>
      {#if scale.length}{@render scaleColumn('right')}{/if}
    </div>
  </div>
</div>

<style>
  .analyser { --segment-on: #57d6c4; --segment-glow: rgba(87, 214, 196, 0.45); --segment-off: rgba(190, 205, 205, 0.11); --legend: var(--analyser-legend, #d6d6cf); --strip: #9a9a92; display: flex; flex-direction: column; min-height: 0; }
  /* The window's bezel: a dark frame with a fine highlight, glass recessed inside */
  .frame { flex: 1; min-height: 0; display: flex; flex-direction: column; padding: 5px; border-radius: 5px; background: linear-gradient(180deg, #1b1c1e, #0c0c0d); box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.08), inset 0 -1px 0 rgba(0, 0, 0, 0.8), 0 1px 3px rgba(0, 0, 0, 0.6); }
  .title-strip { height: 13px; margin: 0 2px 4px; border-radius: 1px; background: linear-gradient(180deg, #5c5d5a, #3f403d); color: #d9d8cf; font-size: 7.5px; letter-spacing: 1.6px; display: grid; place-items: center; box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.15); }
  .window { flex: 1; min-height: 0; display: flex; gap: 6px; padding: 8px 8px 5px; border-radius: 3px; position: relative; overflow: hidden; background: linear-gradient(180deg, rgba(20, 30, 32, 0.92), rgba(10, 16, 18, 0.96)); box-shadow: inset 0 2px 6px rgba(0, 0, 0, 0.75); }
  /* A faint reflection across the glass */
  .window::after { content: ""; position: absolute; inset: 0; background: linear-gradient(162deg, rgba(255, 255, 255, 0.08) 0%, rgba(255, 255, 255, 0.015) 36%, transparent 38%); pointer-events: none; }
  .plot { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 3px; }
  .columns { flex: 1; min-height: 0; display: flex; justify-content: space-between; }
  /* Narrow columns with wide gaps between them */
  .column { flex: 1; display: flex; justify-content: center; gap: 1px; min-width: 0; }
  .bar { flex: 1; display: flex; flex-direction: column; gap: 1px; max-width: min(12px, 46%); }
  .bar i { flex: 1; min-height: 1px; border-radius: 0.5px; background: var(--segment-off); }
  .bar i.on { background: var(--segment-on); box-shadow: 0 0 3px var(--segment-glow); }
  .full-gap { width: 9%; } /* the broadband meter stands apart from the bands */
  .scale { display: flex; flex-direction: column; justify-content: space-between; color: var(--legend); font-size: 7.5px; line-height: 1; font-variant-numeric: tabular-nums; }
  .labelled .scale { padding-bottom: 25px; } /* the scale spans the columns only, not the legend under them */
  .scale span { display: flex; align-items: center; gap: 3px; } .scale.right span { flex-direction: row-reverse; }
  .scale b { font-weight: 400; min-width: 10px; text-align: right; } .scale.right b { text-align: left; }
  .scale em { width: 7px; height: 1px; background: var(--legend); opacity: 0.6; }
  .legend { display: flex; color: var(--legend); font-size: 7.5px; } .legend-bands { flex: 1; display: flex; justify-content: space-between; }
  .legend-bands span { flex: 1; text-align: center; white-space: nowrap; }
  .full-label { flex: 1 0 0; max-width: calc(100% / 11); }
  .legend-captions { display: flex; color: var(--legend); font-size: 7px; letter-spacing: 1px; }
  /* FREQUENCY (Hz) sits in a bracket that spans the bands, its rule running out of both sides */
  .bracket { flex: 1; display: flex; align-items: center; gap: 5px; padding: 0 3%; white-space: nowrap; }
  .bracket::before, .bracket::after { content: ""; flex: 1; height: 4px; margin-top: -4px; border-bottom: 1px solid var(--legend); opacity: 0.6; } .bracket::before { border-left: 1px solid var(--legend); } .bracket::after { border-right: 1px solid var(--legend); }
  .full-caption { display: flex; justify-content: flex-end; white-space: nowrap; }
  .labelled:not(.compact) .full-caption span { margin-right: -26px; } /* right-aligned with the scale on the right */
  .compact .frame { padding: 2px; border-radius: 3px; } .compact .window { padding: 2px 1px; gap: 0; }
  .compact .bar { max-width: 70%; } .compact .bar i.on { box-shadow: none; }
</style>
