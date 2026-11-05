<script lang="ts" module>
  let meterCount = 0
</script>
<script lang="ts">
  import { app, ui, meters, writeSetting } from '../state.svelte'
  import PixelMeter from './PixelMeter.svelte'
  // Live from our Squeezelite build's sample export; with no audio the needles rest below -20 and the lamp dims
  let { size = 'small' }: { size?: 'small' | 'large' } = $props()
  let now = $state(Date.now())
  $effect(() => { const ticker = setInterval(() => now = Date.now(), 500); return () => clearInterval(ticker) })
  const active = $derived(now - app.levels.at < 1500 && app.levels.vu.some(decibels => decibels > -60))
  // 0 VU = the reference RMS level (Settings › Meters, -18 dBFS by default); below -20 VU the needle sinks to its rest
  const needleAngle = (decibelsFs: number, rest: number) => {
    const vu = decibelsFs - app.meterSettings.referenceDbfs
    if (vu <= scaleMarks[0][0]) return rest + (scaleMarks[0][1] - rest) * Math.max(0, Math.min(1, (vu + 30) / 10))
    for (let mark = 1; mark < scaleMarks.length; mark++) if (vu <= scaleMarks[mark][0]) { const [lowDb, lowAngle] = scaleMarks[mark - 1], [highDb, highAngle] = scaleMarks[mark]; return lowAngle + (highAngle - lowAngle) * (vu - lowDb) / (highDb - lowDb) }
    return Math.min(52, scaleMarks[scaleMarks.length - 1][1] + (vu - 3) * 4)
  }
  const barLevel = (amplitude: number) => Math.max(0, Math.min(1, (20 * Math.log10(amplitude + 1e-9) + app.meterSettings.barRangeDb) / app.meterSettings.barRangeDb))
  const meterStyles = ['vu', 'oledvu', 'oled']
  const toggle = () => { ui.meterStyle = meterStyles[(meterStyles.indexOf(ui.meterStyle) + 1) % meterStyles.length]; writeSetting('meterStyle', ui.meterStyle) }
  const gradientId = `vu-lamp-${meterCount++}`
  // Classic VU scale: dB mark -> needle angle (degrees from vertical), pivot sits below the face like a real meter
  const scaleMarks: [number, number][] = [[-20, -48], [-10, -31], [-7, -21], [-5, -12], [-3, -1], [-2, 6], [-1, 13], [0, 21], [1, 29], [2, 37], [3, 46]]
  const pivotX = 60, pivotY = 88
  const point = (angle: number, radius: number) => [pivotX + radius * Math.sin(angle * Math.PI / 180), pivotY - radius * Math.cos(angle * Math.PI / 180)]
  const arc = (fromAngle: number, toAngle: number, radius: number) => { const [startX, startY] = point(fromAngle, radius); const [endX, endY] = point(toAngle, radius); return `M${startX} ${startY} A${radius} ${radius} 0 0 1 ${endX} ${endY}` }
  const restAngle = -54
  // OLED VU: one even scale line; above it, a tick per mark and dense detents across the 0..+3 zone (like the Transporter display)
  // Its pivot sits just below the display, closer than the analogue meter's, so the scale is a wider arc and the resting needle lies flatter
  const oledPivotY = 78, oledSpread = 1.25, oledRadius = 42, oledRestAngle = -68
  const oledPoint = (angle: number, radius: number) => [pivotX + radius * Math.sin(angle * oledSpread * Math.PI / 180), oledPivotY - radius * Math.cos(angle * oledSpread * Math.PI / 180)]
  const oledArc = (fromAngle: number, toAngle: number, radius: number) => { const [startX, startY] = oledPoint(fromAngle, radius); const [endX, endY] = oledPoint(toAngle, radius); return `M${startX} ${startY} A${radius} ${radius} 0 0 1 ${endX} ${endY}` }
  const detentAngles = Array.from({ length: 16 }, (unused, index) => 21 + index * 25 / 15)
</script>

<button class="meters {size}" class:idle={!active} onclick={toggle} title="Click to switch meter style">
  {#if ui.meterStyle === 'vu'}
    {#each ['L', 'R'] as channel, channelIndex}
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
        <g clip-path="url(#{gradientId}-clip)"><line x1={pivotX} y1={pivotY} x2={pivotX} y2={pivotY - 60} class="needle" transform="rotate({needleAngle(meters.vu[channelIndex] ?? -90, restAngle)} {pivotX} {pivotY})" /></g>
        <rect x="5" y="60" width="110" height="7" class="pivot-cover" />
      </svg>
    {/each}
  {:else if ui.meterStyle === 'oledvu'}
    {#each ['L', 'R'] as channel, channelIndex}
      <!-- Cropped to the scale: the panel holds just the arc, numbers, needle and channel, centred top to bottom -->
      <svg viewBox="0 17 120 54" class="vu oledvu" aria-hidden="true">
        <defs><filter id={gradientId + '-glow'} x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="1.4" result="blur" /><feMerge><feMergeNode in="blur" /><feMergeNode in="SourceGraphic" /></feMerge></filter></defs>
        <rect x="0" y="17" width="120" height="54" rx="5" class="glass" />
        <g filter="url(#{gradientId}-glow)" class="display">
          <path d={oledArc(-48, 46, oledRadius)} class="oled-line" />
          {#each detentAngles as angle}{@const [detentX1, detentY1] = oledPoint(angle, oledRadius)}{@const [detentX2, detentY2] = oledPoint(angle, oledRadius + 4.5)}<line x1={detentX1} y1={detentY1} x2={detentX2} y2={detentY2} class="oled-tick detent" />{/each}
          {#each scaleMarks as [decibels, angle]}
            {@const [innerX, innerY] = oledPoint(angle, oledRadius)}{@const [outerX, outerY] = oledPoint(angle, oledRadius + 3.5)}{@const [labelX, labelY] = oledPoint(angle, oledRadius + 10)}
            {#if decibels < 0}<line x1={innerX} y1={innerY} x2={outerX} y2={outerY} class="oled-tick" />{/if}
            {#if decibels !== -2 && decibels !== -1 && decibels !== 1}<text x={labelX} y={labelY} class="oled-number">{Math.abs(decibels)}</text>{/if}
          {/each}
          <text x="60" y="52" class="oled-label">VU</text>
          <g clip-path="url(#{gradientId}-oled-clip)"><line x1={pivotX} y1={oledPivotY} x2={pivotX} y2={oledPivotY - 48} class="oled-needle" transform="rotate({needleAngle(meters.vu[channelIndex] ?? -90, oledRestAngle)} {pivotX} {oledPivotY})" /></g>
        </g>
        <text x="60" y="64" class="oled-channel">{channel}</text>
        <defs><clipPath id={gradientId + '-oled-clip'}><rect x="5" y="18" width="110" height="48" /></clipPath></defs>
      </svg>
    {/each}
  {:else}
    <div class="oled"><div class="channels"><span>L</span><span>R</span></div><PixelMeter orientation="horizontal" levels={meters.peak.map(barLevel)} width={size === 'large' ? 300 : 110} height={size === 'large' ? 38 : 18} /></div>
  {/if}
</button>

<style>
  .meters { display: flex; gap: 4px; background: none; border: 0; padding: 0; cursor: pointer; align-items: center; }
  .small .vu { width: 60px; height: 36px; } .large .vu { width: 190px; height: 114px; }
  .small .oledvu { height: 27px; } .large .oledvu { height: 85.5px; }
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
  .needle { stroke: #111; stroke-width: 0.9; } /* ballistics in the shell, easing per frame in state.svelte.ts */
  .pivot-cover { fill: #15161a; }
  .glass { fill: #030507; stroke: #1c1f24; stroke-width: 1; }
  .oled-line { fill: none; stroke: #3fb8ff; stroke-width: 1.4; }
  .oled-tick { stroke: #3fb8ff; stroke-width: 0.9; } .oled-tick.detent { stroke-width: 0.8; }
  .oled-number { fill: #4cc0ff; font-size: 6px; text-anchor: middle; dominant-baseline: middle; font-family: "Segoe UI", Arial, sans-serif; }
  .oled-label { fill: #4cc0ff; font-size: 7.5px; font-weight: 700; text-anchor: middle; font-family: "Segoe UI", Arial, sans-serif; letter-spacing: 0.5px; }
  .oled-needle { stroke: #d8f4ff; stroke-width: 1; }
  .oled-channel { fill: rgba(76, 192, 255, 0.35); font-size: 5px; text-anchor: middle; font-family: var(--font-display); }
  .idle .display { opacity: 0.55; }
  .oled { display: flex; gap: 6px; align-items: center; background: #040607; padding: 6px 8px; border-radius: 6px; }
  .channels { display: flex; flex-direction: column; justify-content: space-between; align-self: stretch; font-family: var(--font-display); font-size: 9px; color: var(--readout); }
  .large .channels { font-size: 12px; }
</style>
