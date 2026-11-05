<script lang="ts" module>
  let meterCount = 0
</script>
<script lang="ts">
  import { ui, writeSetting } from '../state.svelte'
  // Squeezelite on Windows has no level output yet (SPEC.md › Risks), so the needles rest at -20 with the lamp dimmed
  let { size = 'small', active = false }: { size?: 'small' | 'large'; active?: boolean } = $props()
  const toggle = () => { ui.meterStyle = ui.meterStyle === 'vu' ? 'oled' : 'vu'; writeSetting('meterStyle', ui.meterStyle) }
  const gradientId = `vu-lamp-${meterCount++}`
  // Classic VU scale: dB mark -> needle angle (degrees from vertical), pivot sits below the face like a real meter
  const scaleMarks: [number, number][] = [[-20, -48], [-10, -31], [-7, -21], [-5, -12], [-3, -1], [-2, 6], [-1, 13], [0, 21], [1, 29], [2, 37], [3, 46]]
  const pivotX = 60, pivotY = 88
  const point = (angle: number, radius: number) => [pivotX + radius * Math.sin(angle * Math.PI / 180), pivotY - radius * Math.cos(angle * Math.PI / 180)]
  const arc = (fromAngle: number, toAngle: number, radius: number) => { const [startX, startY] = point(fromAngle, radius); const [endX, endY] = point(toAngle, radius); return `M${startX} ${startY} A${radius} ${radius} 0 0 1 ${endX} ${endY}` }
  const restAngle = -54
</script>

<button class="meters {size}" class:idle={!active} onclick={toggle} title={active ? 'Click to switch meter style' : 'Level meters (waiting for level data from Squeezelite). Click to switch style.'}>
  {#if ui.meterStyle === 'vu'}
    {#each ['L', 'R'] as channel}
      <svg viewBox="0 0 120 72" class="vu" aria-hidden="true">
        <defs>
          <!-- Warm lamp below the face: brightest at bottom centre, falling off upward and outward -->
          <radialGradient id={gradientId} cx="50%" cy="100%" r="95%" fx="50%" fy="105%">
            <stop offset="0%" class="lamp-hot" /><stop offset="38%" class="lamp-warm" /><stop offset="75%" class="lamp-mid" /><stop offset="100%" class="lamp-edge" />
          </radialGradient>
          <clipPath id={gradientId + '-clip'}><rect x="5" y="5" width="110" height="55" /></clipPath>
        </defs>
        <rect x="0" y="0" width="120" height="72" rx="5" class="bezel" />
        <rect x="5" y="5" width="110" height="62" rx="2" fill="url(#{gradientId})" class="face" />
        <path d={arc(-48, 21, 52)} class="scale" />
        <path d={arc(21, 46, 52)} class="red-line" />
        <path d={arc(21, 46, 54.5)} class="red-band" />
        {#each scaleMarks as [decibels, angle]}
          {@const [innerX, innerY] = point(angle, 52)}{@const [outerX, outerY] = point(angle, decibels === 0 || decibels === -20 ? 58 : 56)}{@const [labelX, labelY] = point(angle, 62.5)}
          <line x1={innerX} y1={innerY} x2={outerX} y2={outerY} class="tick" class:hot={decibels > 0} />
          <text x={labelX} y={labelY} class="number" class:hot={decibels > 0}>{Math.abs(decibels)}</text>
        {/each}
        {#each [-48, -31, -21, -12, -1, 6, 13, 21] as angle, index}{#if index < 7}{@const [minorX1, minorY1] = point(angle + (scaleMarks[index + 1][1] - angle) / 2, 52)}{@const [minorX2, minorY2] = point(angle + (scaleMarks[index + 1][1] - angle) / 2, 54.5)}<line x1={minorX1} y1={minorY1} x2={minorX2} y2={minorY2} class="tick minor" />{/if}{/each}
        <text x="16" y="22" class="vu-label">VU</text>
        <text x="105" y="57" class="sign hot">+</text><text x="14" y="57" class="sign">−</text>
        <text x="60" y="58" class="channel">{channel}</text>
        <g clip-path="url(#{gradientId}-clip)"><line x1={pivotX} y1={pivotY} x2={pivotX} y2={pivotY - 60} class="needle" transform="rotate({restAngle} {pivotX} {pivotY})" /></g>
        <rect x="5" y="60" width="110" height="7" class="pivot-cover" />
      </svg>
    {/each}
  {:else}
    <div class="oled">
      {#each ['L', 'R'] as channel}<div class="bar"><span class="ch">{channel}</span>{#each Array(16) as _, segment}<span class="segment" class:hot={segment >= 13}></span>{/each}</div>{/each}
    </div>
  {/if}
</button>

<style>
  .meters { display: flex; gap: 4px; background: none; border: 0; padding: 0; cursor: pointer; align-items: center; }
  .small .vu { width: 60px; height: 36px; } .large .vu { width: 190px; height: 114px; }
  .bezel { fill: #15161a; stroke: #2c2e34; stroke-width: 1; }
  .lamp-hot { stop-color: #fff6d8; } .lamp-warm { stop-color: #f6e3b0; } .lamp-mid { stop-color: #d9bf86; } .lamp-edge { stop-color: #9c8457; }
  .idle .lamp-hot { stop-color: #e9dcb6; } .idle .lamp-warm { stop-color: #d8c595; } .idle .lamp-mid { stop-color: #b59d6c; } .idle .lamp-edge { stop-color: #7a6744; }
  .face { stroke: rgba(0, 0, 0, 0.5); stroke-width: 0.8; }
  .scale { fill: none; stroke: #1d1a14; stroke-width: 0.9; }
  .red-line { fill: none; stroke: #c4291c; stroke-width: 0.9; }
  .red-band { fill: none; stroke: #c4291c; stroke-width: 3; }
  .tick { stroke: #1d1a14; stroke-width: 0.9; } .tick.minor { stroke-width: 0.6; } .tick.hot { stroke: #c4291c; }
  .number { fill: #1d1a14; font-size: 6.5px; text-anchor: middle; dominant-baseline: middle; font-family: "Segoe UI", Arial, sans-serif; font-weight: 600; }
  .number.hot { fill: #c4291c; }
  .vu-label { fill: #1d1a14; font-size: 9px; font-weight: 700; font-family: "Segoe UI", Arial, sans-serif; }
  .sign { fill: #1d1a14; font-size: 8px; font-weight: 700; text-anchor: middle; font-family: Arial, sans-serif; } .sign.hot { fill: #c4291c; }
  .channel { fill: rgba(29, 26, 20, 0.55); font-size: 6px; text-anchor: middle; font-family: var(--font-display); }
  .needle { stroke: #111; stroke-width: 0.9; transition: transform 300ms ease-out; }
  .pivot-cover { fill: #15161a; }
  .oled { display: flex; flex-direction: column; gap: 3px; background: #050607; padding: 4px 6px; border-radius: 4px; }
  .bar { display: flex; gap: 2px; align-items: center; }
  .ch { font-family: var(--font-display); font-size: 8px; color: var(--readout); width: 8px; }
  .segment { width: 4px; height: 7px; background: rgba(94, 231, 255, 0.12); border-radius: 1px; }
  .segment.hot { background: rgba(255, 90, 70, 0.15); }
  .large .segment { width: 9px; height: 16px; } .large .ch { font-size: 12px; width: 14px; }
</style>
