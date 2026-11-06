<script lang="ts" module>
  let meterCount = 0
</script>
<script lang="ts">
  import { app, ui, meters, writeSetting } from '../state.svelte'
  import Analyser from './Analyser.svelte'
  import MeterPanel, { panelThemes } from './MeterPanel.svelte'
  // Live from our Squeezelite build's sample export; with no audio the needles rest below -20 and the lamp dims
  let { size = 'small' }: { size?: 'small' | 'large' | 'full' } = $props()
  let now = $state(Date.now())
  $effect(() => { const ticker = setInterval(() => now = Date.now(), 500); return () => clearInterval(ticker) })
  const active = $derived(now - app.levels.at < 1500 && app.levels.vu.some(decibels => decibels > -60))
  // 0 VU = the reference RMS level (auto, following the music's loudness, or fixed in Settings › Meters); below -20 VU the needle sinks to its rest
  const needleAngle = (decibelsFs: number, rest: number) => {
    const vu = decibelsFs - meters.reference
    if (vu <= scaleMarks[0][0]) return rest + (scaleMarks[0][1] - rest) * Math.max(0, Math.min(1, (vu + 30) / 10))
    for (let mark = 1; mark < scaleMarks.length; mark++) if (vu <= scaleMarks[mark][0]) { const [lowDb, lowAngle] = scaleMarks[mark - 1], [highDb, highAngle] = scaleMarks[mark]; return lowAngle + (highAngle - lowAngle) * (vu - lowDb) / (highDb - lowDb) }
    return Math.min(52, scaleMarks[scaleMarks.length - 1][1] + (vu - 3) * 4)
  }
  // Same octaves and scale as the EQ window's analyser
  const spectrumScale = ['30', '24', '18', '12', '6']
  const meterStyles = ['vu', 'deck', 'orange', 'monitor', 'blue', 'green', 'oledvu', 'oled'] // analogue faces after classic amps and decks sit between the VU and the OLED styles
  const bandLabels = ['31', '63', '125', '250', '500', '1k', '2k', '4k', '8k', '16k'] // octave centres (Hz)
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

<button class="meters {size}" class:idle={!active} onclick={toggle} oncontextmenu={(event) => { event.preventDefault(); ui.metersFull = !ui.metersFull }} title="Click to switch meter style · right-click for full window">
  {#if ui.meterStyle === 'vu'}
    {#each ['L', 'R'] as channel, channelIndex}
      <svg viewBox="0 0 120 72" class="vu" aria-hidden="true">
        <defs>
          <!-- Warm lamp below the face: brightest at bottom centre, falling off upward and outward -->
          <radialGradient id={gradientId + channel} cx={channelIndex ? '53%' : '47%'} cy="100%" r="95%" fx={channelIndex ? '57%' : '44%'} fy="105%">
            <stop offset="0%" class="lamp-hot" /><stop offset="38%" class="lamp-warm" /><stop offset="75%" class="lamp-mid" /><stop offset="100%" class="lamp-edge" />
          </radialGradient>
          <!-- Bulb shading: the bezel lip shadows the top, corners fall off into the dark, the lamp lights the window's bottom edge -->
          <linearGradient id={gradientId + '-lip'} x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stop-color="#1a0e00" stop-opacity="0.32" /><stop offset="5%" stop-color="#1a0e00" stop-opacity="0.12" /><stop offset="12%" stop-color="#1a0e00" stop-opacity="0.02" /><stop offset="22%" stop-color="#1a0e00" stop-opacity="0" /></linearGradient>
          <radialGradient id={gradientId + channel + '-falloff'} cx={channelIndex ? '56%' : '45%'} cy="92%" r="80%"><stop offset="45%" stop-color="#2a1500" stop-opacity="0" /><stop offset="70%" stop-color="#2a1500" stop-opacity="0.06" /><stop offset="100%" stop-color="#2a1500" stop-opacity="0.26" /></radialGradient>
          <!-- Real bulbs are uneven: a second softer hotspot off to one side and low-frequency mottling, different per meter -->
          <radialGradient id={gradientId + channel + '-blob'} cx={channelIndex ? '28%' : '74%'} cy={channelIndex ? '62%' : '70%'} r="42%"><stop offset="0%" stop-color="#fff2cc" stop-opacity="0.32" /><stop offset="100%" stop-color="#fff2cc" stop-opacity="0" /></radialGradient>
          <filter id={gradientId + channel + '-mottle'} x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency="0.045 0.06" numOctaves="2" seed={channelIndex ? 11 : 4} /><feColorMatrix values="0 0 0 0 0.16  0 0 0 0 0.09  0 0 0 0 0  0 0 0 1.4 -0.55" /></filter>
          <filter id={gradientId + '-inset'} x="-10%" y="-10%" width="120%" height="120%"><feGaussianBlur stdDeviation="2.2" /></filter>
          <filter id={gradientId + '-grain'} x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency={size === 'full' ? 6 : 1.6} numOctaves="1" seed={channelIndex ? 3 : 8} /><feColorMatrix values="0 0 0 0 0.2  0 0 0 0 0.13  0 0 0 0 0.05  0 0 0 1.1 -0.45" /></filter>
          <filter id={gradientId + '-soft'} filterUnits="userSpaceOnUse" x="0" y="0" width="120" height="72"><feGaussianBlur stdDeviation="0.7" /></filter>
          <linearGradient id={gradientId + '-sheen'} x1="0" y1="0" x2="1" y2="0.6"><stop offset="0%" stop-color="#fff" stop-opacity="0" /><stop offset="30%" stop-color="#fff" stop-opacity="0.13" /><stop offset="44%" stop-color="#fff" stop-opacity="0.02" /><stop offset="58%" stop-color="#fff" stop-opacity="0" /></linearGradient>
          <!-- Glass: the thick edge bends light unevenly; faint smudges and dust on the pane -->
          <filter id={gradientId + channel + '-warp'} filterUnits="userSpaceOnUse" x="0" y="0" width="120" height="72"><feTurbulence type="fractalNoise" baseFrequency="0.09" numOctaves="2" seed={channelIndex ? 21 : 13} result="warp" /><feDisplacementMap in="SourceGraphic" in2="warp" scale="0.9" xChannelSelector="R" yChannelSelector="G" /></filter>
          <filter id={gradientId + channel + '-dust'} filterUnits="userSpaceOnUse" x="5" y="5" width="110" height="62"><feTurbulence type="fractalNoise" baseFrequency={size === 'full' ? 3 : 1.2} numOctaves="1" seed={channelIndex ? 31 : 17} /><feColorMatrix values="0 0 0 0 1  0 0 0 0 0.98  0 0 0 0 0.94  0 0 0 9 -7.4" /></filter>
          <filter id={gradientId + '-smudge'} filterUnits="userSpaceOnUse" x="0" y="0" width="120" height="72"><feGaussianBlur stdDeviation="5" /></filter>
          <linearGradient id={gradientId + '-edge-light'} x1="0" y1="0" x2="1" y2="1"><stop offset="0%" stop-color="#fff6de" stop-opacity="0.9" /><stop offset="35%" stop-color="#fff6de" stop-opacity="0.35" /><stop offset="70%" stop-color="#fff6de" stop-opacity="0.12" /><stop offset="100%" stop-color="#fff6de" stop-opacity="0.4" /></linearGradient>
          <clipPath id={gradientId + '-clip'}><rect x="5" y="5" width="110" height="62" rx="2" /></clipPath>
        </defs>
        <rect x="0" y="0" width="120" height="72" rx="5" class="bezel" />
        <rect x="5" y="5" width="110" height="62" rx="2" fill="url(#{gradientId}{channel})" class="face" />
        <path d={arc(-48, 21, 52)} class="scale" />
        <path d={arc(21, 46, 52)} class="red-line" />
        <path d={arc(21, 46, 54.5)} class="red-band" />
        {#each scaleMarks as [decibels, angle]}
          {@const [innerX, innerY] = point(angle, 52)}{@const [outerX, outerY] = point(angle, decibels === 0 || decibels === -20 ? 58 : 56)}{@const [labelX, labelY] = point(angle, 62.5)}
          <line x1={innerX} y1={innerY} x2={outerX} y2={outerY} class="tick" class:hot={decibels > 0} />
          <text x={labelX} y={labelY} class="number" class:hot={decibels > 0}>{Math.abs(decibels)}</text>
        {/each}
        {#each [-48, -31, -21, -12, -1, 6, 13, 21] as angle, index}{#if index < 7}{@const [minorX1, minorY1] = point(angle + (scaleMarks[index + 1][1] - angle) / 2, 52)}{@const [minorX2, minorY2] = point(angle + (scaleMarks[index + 1][1] - angle) / 2, 54.5)}<line x1={minorX1} y1={minorY1} x2={minorX2} y2={minorY2} class="tick minor" />{/if}{/each}
        <!-- Second scale under the arc: percent of 0 VU -->
        <path d={arc(needleAngle(-13.98 + meters.reference, restAngle), 21, 48.5)} class="scale percent" />
        {#each [[20, -13.98], [40, -7.96], [60, -4.44], [80, -1.94], [100, 0]] as [percent, decibels]}
          {@const angle = needleAngle(decibels + meters.reference, restAngle)}{@const [tickX1, tickY1] = point(angle, 48.5)}{@const [tickX2, tickY2] = point(angle, 46.6)}{@const [textX, textY] = point(angle, 43.6)}
          <line x1={tickX1} y1={tickY1} x2={tickX2} y2={tickY2} class="tick minor" /><text x={textX} y={textY} class="percent-number">{percent}</text>
        {/each}
        <text x="16" y="22" class="vu-label">VU</text>
        <circle cx="106" cy="13.5" r="2.8" class="pilot" /><circle cx="105.2" cy="12.7" r="0.9" class="pilot-glint" />
        <text x="105" y="57" class="sign hot">+</text><text x="14" y="57" class="sign">−</text>
        <text x="60" y="58" class="channel">{channel}</text>
        <rect x="5" y="5" width="110" height="62" fill="url(#{gradientId}{channel}-blob)" class="shade" /><rect x="5" y="5" width="110" height="62" filter="url(#{gradientId}{channel}-mottle)" class="shade mottle" />
        <rect x="5" y="5" width="110" height="62" fill="url(#{gradientId}{channel}-falloff)" class="shade" /><rect x="5" y="5" width="110" height="62" fill="url(#{gradientId}-lip)" class="shade" /><g clip-path="url(#{gradientId}-clip)"><rect x="5" y="6.6" width="110" height="62" class="shade inset" filter="url(#{gradientId}-inset)" /></g>
        {#if size !== 'small'}<rect x="5" y="5" width="110" height="62" filter="url(#{gradientId}-grain)" class="shade grain" />
        <g clip-path="url(#{gradientId}-clip)"><line x1={pivotX + 1.6} y1={pivotY} x2={pivotX + 1.6} y2={pivotY - 60} class="needle-shadow" filter="url(#{gradientId}-soft)" transform="rotate({needleAngle(meters.vu[channelIndex] ?? -90, restAngle)} {pivotX} {pivotY - 1.2})" /></g>{/if}
        <g clip-path="url(#{gradientId}-clip)"><line x1={pivotX} y1={pivotY} x2={pivotX} y2={pivotY - 60} class="needle" transform="rotate({needleAngle(meters.vu[channelIndex] ?? -90, restAngle)} {pivotX} {pivotY})" /></g>
        <rect x="7" y="65.6" width="106" height="1" class="lit-edge" />
        <!-- Acrylic window edge: light bends through the thick rim, a bright band with a darker inner seam -->
        <rect x="5.7" y="5.7" width="108.6" height="60.6" rx="1.6" class="rim-glow" /><rect x="7.3" y="7.3" width="105.4" height="57.4" rx="1" class="rim-seam" /><path d="M6 6.2 H114" class="rim-catch" />
        {#if size !== 'small'}<rect x="5" y="5" width="110" height="62" rx="2" fill="url(#{gradientId}-sheen)" class="shade" />
          <g clip-path="url(#{gradientId}-clip)" class="shade">
            <rect x="6.3" y="6.3" width="107.4" height="59.4" rx="1.4" fill="none" stroke="url(#{gradientId}-edge-light)" stroke-width="2.4" filter="url(#{gradientId}{channel}-warp)" class="refraction" />
            <rect x="8.4" y="8.4" width="103.2" height="55.2" rx="1" fill="none" stroke="#3a2508" stroke-width="0.35" filter="url(#{gradientId}{channel}-warp)" class="refraction-seam" />
            <ellipse cx={channelIndex ? 88 : 30} cy={channelIndex ? 22 : 48} rx="16" ry="7" transform="rotate({channelIndex ? -18 : 24} {channelIndex ? 88 : 30} {channelIndex ? 22 : 48})" fill="#fff" filter="url(#{gradientId}-smudge)" class="smudge" />
            <rect x="5" y="5" width="110" height="62" filter="url(#{gradientId}{channel}-dust)" class="dust" />
          </g>{/if}
      </svg>
    {/each}
  {:else if panelThemes[ui.meterStyle]}
    <MeterPanel theme={ui.meterStyle} levels={meters.vu} reference={meters.reference} {size} idle={!active} />
  {:else if ui.meterStyle === 'oledvu'}
    {#each ['L', 'R'] as channel, channelIndex}
      <!-- Cropped to the scale: the panel holds just the arc, numbers, needle and channel, centred top to bottom -->
      <svg viewBox="0 17 120 54" class="vu oledvu" aria-hidden="true">
        <defs><filter id={gradientId + '-glow'} x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="1.4" result="blur" /><feMerge><feMergeNode in="blur" /><feMergeNode in="SourceGraphic" /></feMerge></filter></defs>
        <defs><linearGradient id="oled-glass-sheen" x1="0" y1="0" x2="0.35" y2="1"><stop offset="0" stop-color="rgba(255,255,255,0.10)" /><stop offset="0.45" stop-color="rgba(255,255,255,0.02)" /><stop offset="1" stop-color="rgba(0,0,0,0.12)" /></linearGradient></defs>
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
    <!-- Spectrum analyser (the third style; stored as 'oled' so earlier choices carry over) -->
    <div class="spectrum">{#if size !== 'small'}<Analyser levels={meters.bands} labels={bandLabels} scale={spectrumScale} segments={24} title="SPECTRUM ANALYZER" fullRange={Math.max(0, Math.min(1, (Math.max(meters.vu[0] ?? -90, meters.vu[1] ?? -90) + 40) / 40))} />{:else}<Analyser levels={meters.bands} compact={true} segments={12} />{/if}</div>
  {/if}
</button>

<style>
  .meters { display: flex; gap: 4px; background: none; border: 0; padding: 0; cursor: pointer; align-items: center; }
  .small .vu { width: 60px; height: 36px; } .large .vu { width: 190px; height: 114px; }
  /* Whole panels keep their own shape and size: tall round meters, long low monitor strips… */
  .small :global(.panel) { height: 34px; width: auto; max-width: 130px; } .large :global(.panel) { width: auto; }
  .large :global(.panel.deck) { height: 104px; } .large :global(.panel.orange) { height: 124px; } .large :global(.panel.monitor) { height: 84px; } .large :global(.panel.blue) { height: 96px; } .large :global(.panel.green) { height: 112px; }
  .full :global(.panel) { height: auto; width: min(var(--full-share) * 1vw, (100vh - 140px) * var(--aspect)); }
  .full :global(.panel.deck) { --full-share: 78; } .full :global(.panel.orange) { --full-share: 58; } .full :global(.panel.monitor) { --full-share: 94; } .full :global(.panel.blue) { --full-share: 86; } .full :global(.panel.green) { --full-share: 90; }
  .small .oledvu { height: 27px; } .large .oledvu { height: 85.5px; }
  .bezel { fill: #15161a; stroke: #2c2e34; stroke-width: 1; }
  .lamp-hot { stop-color: #fff3d6; } .lamp-warm { stop-color: #f8deaa; } .lamp-mid { stop-color: #dcb47a; } .lamp-edge { stop-color: #a17a48; }
  .idle .lamp-hot { stop-color: #f3e3c4; } .idle .lamp-warm { stop-color: #e6cc9c; } .idle .lamp-mid { stop-color: #c9a46e; } .idle .lamp-edge { stop-color: #8d6a3e; }
  .face { stroke: rgba(0, 0, 0, 0.5); stroke-width: 0.8; }
  .scale { fill: none; stroke: #1d1a14; stroke-width: 0.9; stroke-linecap: round; }
  .red-line { fill: none; stroke: #c4291c; stroke-width: 0.9; }
  .red-band { fill: none; stroke: #c4291c; stroke-width: 3; }
  .tick { stroke: #1d1a14; stroke-width: 0.9; stroke-linecap: round; } .tick.minor { stroke-width: 0.6; } .tick.hot { stroke: #c4291c; }
  .number { fill: #1d1a14; font-size: 6.5px; text-anchor: middle; dominant-baseline: middle; font-family: "Segoe UI", Arial, sans-serif; font-weight: 600; }
  .number.hot { fill: #c4291c; }
  .vu-label { fill: #1d1a14; font-size: 9px; font-weight: 700; font-family: "Segoe UI", Arial, sans-serif; }
  .sign { fill: #1d1a14; font-size: 8px; font-weight: 700; text-anchor: middle; font-family: Arial, sans-serif; } .sign.hot { fill: #c4291c; }
  .channel { fill: rgba(29, 26, 20, 0.55); font-size: 6px; text-anchor: middle; font-family: var(--font-display); }
  .needle { stroke: #111; stroke-width: 0.9; } /* ballistics in the shell, easing per frame in state.svelte.ts */
  .inset { fill: none; stroke: #1c1206; stroke-width: 4; opacity: 0.55; }
  .percent { stroke-width: 0.5; } .percent-number { fill: #1d1a14; font-size: 3.6px; text-anchor: middle; dominant-baseline: middle; font-family: "Segoe UI", Arial, sans-serif; }
  .pilot { fill: #2b2620; stroke: #7a6646; stroke-width: 0.5; } .pilot-glint { fill: #fff; opacity: 0.45; }
  .shade { pointer-events: none; } .mottle { opacity: 0.07; }
  .rim-glow { fill: none; stroke: #ffe6b4; stroke-width: 1.3; opacity: 0.4; } .rim-seam { fill: none; stroke: #3a2508; stroke-width: 0.45; opacity: 0.35; } .rim-catch { stroke: #fff8e6; stroke-width: 0.5; opacity: 0.6; }
  .grain { opacity: 0.45; } .full .grain { opacity: 0.18; } .needle-shadow { stroke: #2a1a05; stroke-width: 1.4; opacity: 0.22; }
  .idle .rim-glow { opacity: 0.22; } .idle .rim-catch { opacity: 0.3; }
  .refraction { opacity: 0.42; } .refraction-seam { opacity: 0.22; } .smudge { opacity: 0.06; } .dust { opacity: 0.22; }
  .idle .refraction { opacity: 0.35; }
  .lit-edge { fill: #ffe7b0; opacity: 0.75; } .idle .lit-edge { opacity: 0.35; }
  /* OLED VU: a frosted glass pane over whatever is behind, with a fine top-lit edge */
  .glass { fill: url(#oled-glass-sheen); stroke: rgba(255, 255, 255, 0.16); stroke-width: 0.6; }
  .oledvu { border-radius: 6px; backdrop-filter: blur(10px) saturate(1.3); background: rgba(8, 14, 22, 0.28); box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.12), 0 4px 14px rgba(0, 0, 0, 0.25); }
  .oled-line { fill: none; stroke: #3fb8ff; stroke-width: 1.4; }
  .oled-tick { stroke: #3fb8ff; stroke-width: 0.9; } .oled-tick.detent { stroke-width: 0.8; }
  .oled-number { fill: #4cc0ff; font-size: 6px; text-anchor: middle; dominant-baseline: middle; font-family: "Segoe UI", Arial, sans-serif; }
  .oled-label { fill: #4cc0ff; font-size: 7.5px; font-weight: 700; text-anchor: middle; font-family: "Segoe UI", Arial, sans-serif; letter-spacing: 0.5px; }
  .oled-needle { stroke: #d8f4ff; stroke-width: 1; }
  .oled-channel { fill: rgba(76, 192, 255, 0.35); font-size: 5px; text-anchor: middle; font-family: var(--font-display); }
  .idle .display { opacity: 0.55; }
  .spectrum { display: flex; } .large .spectrum { width: 320px; height: 150px; }
  /* Full window (right-click): as big as the window allows */
  .full { gap: 2vw; } .full .vu { width: min(46vw, (100vh - 140px) * 1.66); height: auto; } .full .spectrum { width: min(92vw, (100vh - 140px) * 2.2); height: min(70vh, 46vw); } .small .spectrum { width: 64px; height: 29px; }
  .spectrum :global(.analyser) { flex: 1; }
  .oledvu :is(.oled-line, .oled-tick, .oled-number, .oled-label, .oled-needle) { filter: drop-shadow(0 0 1.2px rgba(63, 184, 255, 0.7)); }
</style>
