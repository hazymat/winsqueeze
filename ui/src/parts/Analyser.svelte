<script lang="ts">
  import { t } from '../lib/i18n'
  // Hi-fi spectrum analyser, after the classic EQ front panels: a framed smoked-glass window with a title strip, narrow columns of
  // thin segments (faint grey when off, soft teal when lit), a dotted scale each side and an optional FULL RANGE column.
  // levels: 0..1 per column; with levelsRight each column splits into a left and a right bar (L/R display)
  let { levels, levelsRight = null, labels = [], segments = 30, scale = [], compact = false, title = '', fullRange = null }: { levels: number[]; levelsRight?: number[] | null; labels?: string[]; segments?: number; scale?: string[]; compact?: boolean; title?: string; fullRange?: number | null } = $props()
  const litCount = (level: number) => Math.max(0, Math.min(segments, Math.ceil(level * segments - 0.3)))
  // fullRange: 0..1 broadband level for the separate FULL RANGE column (null = no column)
  // Segments are drawn on a canvas sized in whole device pixels, at whole-pixel positions, so every segment and gap is
  // identical at any DPI, zoom or window position. The (invisible) bar boxes still do the layout, so legends line up.
  let columnsElement = $state<HTMLElement | null>(null), canvasElement = $state<HTMLCanvasElement | null>(null), layoutVersion = $state(0)
  const barLevels = $derived(levels.flatMap((level, index) => levelsRight ? [level, levelsRight[index] ?? 0] : [level]).concat(fullRange !== null ? [fullRange] : []))
  // The canvas is nudged onto the device-pixel grid and its backing store matches its device size exactly, so it maps 1:1
  let canvasPlacement = $state('')
  function sizeCanvas() {
    if (!canvasElement || !columnsElement || !columnsElement.clientWidth) return
    const pixelRatio = window.devicePixelRatio || 1, columnsRect = columnsElement.getBoundingClientRect(), devicePerCss = pixelRatio * columnsRect.width / columnsElement.clientWidth // includes CSS zoom
    const leftPixels = Math.ceil(columnsRect.left * pixelRatio - 0.01), topPixels = Math.ceil(columnsRect.top * pixelRatio - 0.01)
    const widthPixels = Math.floor(columnsRect.right * pixelRatio + 0.01) - leftPixels, heightPixels = Math.floor(columnsRect.bottom * pixelRatio + 0.01) - topPixels
    canvasPlacement = `left: ${(leftPixels - columnsRect.left * pixelRatio) / devicePerCss}px; top: ${(topPixels - columnsRect.top * pixelRatio) / devicePerCss}px; width: ${widthPixels / devicePerCss}px; height: ${heightPixels / devicePerCss}px`
    if (canvasElement.width !== widthPixels || canvasElement.height !== heightPixels) { canvasElement.width = widthPixels; canvasElement.height = heightPixels }
    measure(); layoutVersion++
  }
  $effect(() => {
    if (!canvasElement) return
    const observer = new ResizeObserver(sizeCanvas); observer.observe(columnsElement!); observer.observe(document.documentElement)
    window.addEventListener('resize', sizeCanvas)
    return () => { observer.disconnect(); window.removeEventListener('resize', sizeCanvas) }
  })
  // Where each bar sits, in canvas pixels: measured only when the layout changes. Measuring every frame forced a layout of the whole page per frame, which stalled the meters
  let geometry: { barLefts: number[], barWidthPixels: number, pitchPixels: number, segmentPixels: number } | null = null
  function measure() {
    if (!canvasElement || !columnsElement || !canvasElement.height || !columnsElement.clientHeight) { geometry = null; return }
    const canvasWidth = canvasElement.width, canvasHeight = canvasElement.height, columnsRect = columnsElement.getBoundingClientRect()
    const devicePerCss = canvasHeight / columnsElement.clientHeight, gapPixels = Math.max(1, Math.round(devicePerCss * 0.6))
    const pitchPixels = Math.max(gapPixels * 2, Math.floor(canvasHeight / segments)), segmentPixels = pitchPixels - gapPixels
    // Bars share one whole-pixel width and repeat on a whole-pixel column pitch (scaled about the middle), so gaps match too
    const toPixels = (cssX: number) => (cssX - columnsRect.left) / columnsRect.width * canvasWidth
    const columnElements = Array.from(columnsElement.querySelectorAll<HTMLElement>('.column')), columnStarts = columnElements.map(column => toPixels(column.getBoundingClientRect().left))
    const columnPitch = columnStarts.length > 1 ? columnStarts[1] - columnStarts[0] : canvasWidth, pitchScale = Math.max(1, Math.round(columnPitch)) / columnPitch
    const firstStart = Math.round(canvasWidth / 2 + (columnStarts[0] - canvasWidth / 2) * pitchScale)
    const barLefts: number[] = []; let barWidthPixels = 0
    columnElements.forEach((column, columnIndex) => column.querySelectorAll<HTMLElement>('.bar').forEach(bar => {
      const barRect = bar.getBoundingClientRect()
      barWidthPixels ||= Math.max(1, Math.round(barRect.width / columnsRect.width * canvasWidth))
      barLefts.push(firstStart + Math.round((columnStarts[columnIndex] - columnStarts[0]) * pitchScale) + Math.round((toPixels(barRect.left) - columnStarts[columnIndex]) * pitchScale))
    }))
    geometry = { barLefts, barWidthPixels, pitchPixels, segmentPixels }; coloursAt = 0
  }
  $effect(() => { barLevels.length; segments; const frame = requestAnimationFrame(() => { measure(); layoutVersion++ }); return () => cancelAnimationFrame(frame) }) // bars added or removed
  // Peak hold: a floating segment marks each bar's recent top, holds ~0.8 s, then drifts down; peakTick keeps it falling after the music stops
  const peakHoldMs = 800, peakFallPerSecond = 0.9 // in bar heights per second
  let peakLevels: number[] = [], peakHeldAt: number[] = [], peakDrawnAt = 0, peakTick = $state(0), peakFrame = 0, onColour = '', offColour = '', coloursAt = 0
  $effect(() => () => cancelAnimationFrame(peakFrame))
  $effect(() => {
    layoutVersion; peakTick; const context = canvasElement?.getContext('2d')
    if (!context || !geometry || !columnsElement) return
    const { barLefts, barWidthPixels, pitchPixels, segmentPixels } = geometry, canvasHeight = canvasElement!.height, now = performance.now()
    if (now - coloursAt > 1000) { const colours = getComputedStyle(columnsElement); onColour = colours.getPropertyValue('--segment-on'); offColour = colours.getPropertyValue('--segment-off'); coloursAt = now } // skin changes show within a second
    context.clearRect(0, 0, canvasElement!.width, canvasHeight)
    let peaksFloating = false
    const sinceDrawn = Math.min(0.25, (now - (peakDrawnAt || now)) / 1000); peakDrawnAt = now
    barLefts.forEach((leftPixels, barIndex) => {
      const level = barLevels[barIndex] ?? 0, lit = litCount(level)
      if (!(level < (peakLevels[barIndex] ?? 0))) { peakLevels[barIndex] = level; peakHeldAt[barIndex] = now }
      else if (now - peakHeldAt[barIndex] > peakHoldMs) peakLevels[barIndex] = Math.max(level, peakLevels[barIndex] - peakFallPerSecond * sinceDrawn)
      const peakSegment = litCount(peakLevels[barIndex]) - 1
      if (peakSegment >= lit) peaksFloating = true
      for (let segmentIndex = 0; segmentIndex < segments; segmentIndex++) {
        // Lit segments brighten up the scale (dim at the bottom, full at the top); the peak cap is always full brightness
        const isPeak = segmentIndex === peakSegment && peakSegment > 0, isLit = segmentIndex < lit || isPeak
        context.fillStyle = isLit ? onColour : offColour; context.globalAlpha = isLit && !isPeak ? 0.5 + 0.5 * segmentIndex / Math.max(1, segments - 1) : 1
        context.fillRect(leftPixels, canvasHeight - segmentIndex * pitchPixels - segmentPixels, barWidthPixels, segmentPixels)
      }
    })
    context.globalAlpha = 1
    cancelAnimationFrame(peakFrame); if (peaksFloating) peakFrame = requestAnimationFrame(() => peakTick++)
  })
</script>

{#snippet column(columnLevels: number[])}
  <div class="column">{#each columnLevels}<div class="bar"></div>{/each}</div>
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
        <div class="columns" bind:this={columnsElement}><canvas bind:this={canvasElement} style={canvasPlacement}></canvas>
          {#each levels as level, index}{@render column(levelsRight ? [level, levelsRight[index] ?? 0] : [level])}{/each}
          {#if fullRange !== null}<div class="full-gap"></div>{@render column([fullRange])}{/if}
        </div>
        {#if labels.length}
          <div class="legend"><div class="legend-bands">{#each labels as label}<span>{label}</span>{/each}</div>{#if fullRange !== null}<div class="full-gap"></div><span class="full-label"></span>{/if}</div>
          <div class="legend-captions"><span class="bracket">{t('FREQUENCY (Hz)')}</span>{#if fullRange !== null}<div class="full-gap"></div><span class="full-label full-caption"><span>{t('FULL RANGE')}</span></span>{/if}</div>
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
  .columns { flex: 1; min-height: 0; display: flex; position: relative; justify-content: space-between; }
  /* Narrow columns with wide gaps between them */
  .column { flex: 1; display: flex; justify-content: center; gap: 1px; min-width: 0; }
  .bar { flex: 1; max-width: min(12px, 46%); }
  canvas { position: absolute; left: 0; top: 0; width: 100%; height: 100%; pointer-events: none; }
  .analyser:not(.compact) canvas { filter: drop-shadow(0 0 2px var(--segment-glow)); }
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
  .compact .bar { max-width: 70%; }
</style>
