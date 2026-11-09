<script lang="ts" module>
  let meterCount = 0
</script>
<script lang="ts">
  import { untrack } from 'svelte'
  import { app, ui, meters, meterView, writeSetting } from '../state.svelte'
  import { sendToShell } from '../bridge'
  import Analyser from './Analyser.svelte'
  import MeterPanel, { panelThemes } from './MeterPanel.svelte'
  // Live from our Squeezelite build's sample export; with no audio the needles rest below -20 and the lamp dims
  let { size = 'small', zoom = 1, moveWindow = false }: { size?: 'small' | 'large' | 'full', zoom?: number, moveWindow?: boolean } = $props()
  // Grain and dust are drawn in face units, so their frequency follows the real pixel size (window size × full-screen zoom) to stay fine
  let meterWidth = $state(384)
  const textureScale = $derived(Math.max(0.4, zoom * (meterWidth || 384) / 384))
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
  // Full window: dragging the meters moves the window (a click still switches style)
  let pressedAt = null as null | { x: number, y: number }, dragged = false
  const pressMeters = (event: PointerEvent) => { dragged = false; pressedAt = moveWindow && event.button === 0 ? { x: event.clientX, y: event.clientY } : null }
  const dragMeters = (event: PointerEvent) => { if (pressedAt && Math.hypot(event.clientX - pressedAt.x, event.clientY - pressedAt.y) > 4) { pressedAt = null; dragged = true; sendToShell({ type: 'dragWindow' }) } }
  const toggle = () => { if (dragged) { dragged = false; return } ui.meterStyle = meterStyles[(meterStyles.indexOf(ui.meterStyle) + 1) % meterStyles.length]; writeSetting('meterStyle', ui.meterStyle) }
  const gradientId = `vu-lamp-${meterCount++}`
  // Classic VU scale: dB mark -> needle angle (degrees from vertical), pivot sits below the face like a real meter
  const scaleMarks: [number, number][] = [[-20, -48], [-10, -31], [-7, -21], [-5, -12], [-3, -1], [-2, 6], [-1, 13], [0, 21], [1, 29], [2, 37], [3, 46]]
  const pivotX = 60, pivotY = 88
  const point = (angle: number, radius: number) => [pivotX + radius * Math.sin(angle * Math.PI / 180), pivotY - radius * Math.cos(angle * Math.PI / 180)]
  const arc = (fromAngle: number, toAngle: number, radius: number) => { const [startX, startY] = point(fromAngle, radius); const [endX, endY] = point(toAngle, radius); return `M${startX} ${startY} A${radius} ${radius} 0 0 1 ${endX} ${endY}` }
  const restAngle = -54
  // The needle sits a few mm above the face, lit by two bulbs low in the case: each throws its own faint shadow, away from itself, so the pair fans and swaps sides as the needle swings
  const lamps = [[34, 84], [86, 84]], needleHeight = 0.075
  const castShadow = (angle: number, lamp: number[]) => [point(angle, 18), point(angle, 61)].map(([x, y]) => [x + (x - lamp[0]) * needleHeight, y + (y - lamp[1]) * needleHeight])
  // Blade needle: wide where it leaves the movement, tapering to a fine point
  const bladePath = `M${pivotX - 0.9} ${pivotY - 14} L${pivotX - 0.16} ${pivotY - 61} L${pivotX + 0.16} ${pivotY - 61} L${pivotX + 0.9} ${pivotY - 14} Z`
  // Dragging the window jogs the needle (meters.jog); it can't swing below the rest pin
  const vuAngle = (channelIndex: number) => Math.max(restAngle, needleAngle(meters.vu[channelIndex] ?? -90, restAngle) + (meters.jog[channelIndex] ?? 0))
  // Lamp: a filament that warms up when the meter appears (deep orange to warm white over ~1.5 s) and dims, warmer, when the music stops
  const lampKeys: [number, string[]][] = [[0, ['#2a1c10', '#22160c', '#1a1008', '#120b05']], [0.35, ['#d06a20', '#a8501a', '#7a3a12', '#4a240a']], [0.72, ['#f3e3c4', '#e6cc9c', '#c9a46e', '#8d6a3e']], [1, ['#fff3d6', '#f8deaa', '#dcb47a', '#a17a48']]]
  const mixColour = (from: string, to: string, amount: number) => '#' + [1, 3, 5].map(offset => Math.round(parseInt(from.slice(offset, offset + 2), 16) * (1 - amount) + parseInt(to.slice(offset, offset + 2), 16) * amount).toString(16).padStart(2, '0')).join('')
  const lampStops = (heat: number) => { const upper = lampKeys.findIndex(([level]) => level >= heat), [lowLevel, lowColours] = lampKeys[Math.max(0, upper - 1)], [highLevel, highColours] = lampKeys[Math.max(0, upper)]; return lowColours.map((colour, index) => mixColour(colour, highColours[index], highLevel > lowLevel ? (heat - lowLevel) / (highLevel - lowLevel) : 1)) }
  let lampHeat = $state(0)
  $effect(() => {
    const target = active ? 1 : 0.72; let frame = 0, lastTime = performance.now(), lastDrawn = 0
    const warm = (time: number) => {
      const elapsed = Math.min(100, time - lastTime); lastTime = time
      const heat = lampHeat + (target - lampHeat) * (1 - Math.exp(-elapsed / (target > lampHeat ? 380 : 650))) // heats faster than it cools
      const done = Math.abs(target - heat) < 0.004
      if (done || time - lastDrawn > 33) { lampHeat = done ? target : heat; lastDrawn = time } // ~30 redraws a second is smooth enough for a lamp
      frame = done ? 0 : requestAnimationFrame(warm)
    }
    if (ui.meterStyle !== 'vu') untrack(() => lampHeat = target); else if (untrack(() => lampHeat) !== target) frame = requestAnimationFrame(warm)
    return () => cancelAnimationFrame(frame)
  })
  const lamp = $derived(lampStops(lampHeat))
  // Zero-adjust screw: drag sideways to set a fixed 0 VU (right reads higher), double-click for automatic; the note replaces the channel letter while turning
  let screwDrag = $state(null as null | { startX: number, startReference: number, reference: number, turned: boolean })
  let screwNote = $state(''), screwNoteTimer = 0
  const screwAngle = $derived(-30 + ((screwDrag?.reference ?? meters.reference) + 16) * 7.5)
  const showScrewNote = (note: string, holdMs: number) => { clearTimeout(screwNoteTimer); screwNote = note; if (holdMs) screwNoteTimer = setTimeout(() => screwNote = '', holdMs) }
  const setReference = (reference: number) => { app.meterSettings = { referenceDbfs: reference, autoReference: false }; meters.reference = reference; showScrewNote(`0 VU = ${reference} dBFS`.replace('-', '−'), 0) }
  const grabScrew = (event: PointerEvent) => { if (size === 'small' || event.button !== 0) return; event.stopPropagation(); (event.currentTarget as Element).setPointerCapture(event.pointerId); screwDrag = { startX: event.clientX, startReference: Math.round(meters.reference), reference: Math.round(meters.reference), turned: false } }
  const turnScrew = (event: PointerEvent) => { if (!screwDrag) return; const reference = Math.max(-24, Math.min(-8, Math.round(screwDrag.startReference - (event.clientX - screwDrag.startX) / 6))); if (reference !== screwDrag.reference || !screwDrag.turned && Math.abs(event.clientX - screwDrag.startX) > 3) { screwDrag.reference = reference; screwDrag.turned = true; setReference(reference) } }
  const releaseScrew = () => { if (!screwDrag) return; if (screwDrag.turned) { sendToShell({ type: 'setMeterReference', referenceDbfs: screwDrag.reference }); showScrewNote(screwNote, 1500) } screwDrag = null }
  const autoScrew = (event: MouseEvent) => { event.stopPropagation(); if (size === 'small') return; app.meterSettings = { referenceDbfs: app.meterSettings.referenceDbfs, autoReference: true }; sendToShell({ type: 'setMeterReference', auto: true }); showScrewNote('Automatic 0 VU', 1500) }
  // OLED VU: one even scale line; above it, a tick per mark and dense detents across the 0..+3 zone (like the Transporter display)
  // Its pivot sits just below the display, closer than the analogue meter's, so the scale is a wider arc and the resting needle lies flatter
  const oledPivotY = 78, oledSpread = 1.25, oledRadius = 42, oledRestAngle = -68
  const oledPoint = (angle: number, radius: number) => [pivotX + radius * Math.sin(angle * oledSpread * Math.PI / 180), oledPivotY - radius * Math.cos(angle * oledSpread * Math.PI / 180)]
  const oledArc = (fromAngle: number, toAngle: number, radius: number) => { const [startX, startY] = oledPoint(fromAngle, radius); const [endX, endY] = oledPoint(toAngle, radius); return `M${startX} ${startY} A${radius} ${radius} 0 0 1 ${endX} ${endY}` }
  const detentAngles = Array.from({ length: 16 }, (unused, index) => 21 + index * 25 / 15)
</script>

<button class="meters {size}" style="--lamp-glow: {lampHeat}" bind:clientWidth={meterWidth} class:idle={!active} onclick={toggle} onpointerdown={pressMeters} onpointermove={dragMeters} onpointerup={() => pressedAt = null} oncontextmenu={(event) => { event.preventDefault(); ui.metersFull = !ui.metersFull }} title={moveWindow ? 'Click to switch meter style · drag to move the window · right-click to close' : 'Click to switch meter style · right-click for full window'}>
  {#key meterView.epoch}
  {#if ui.meterStyle === 'vu'}
    {#each ['L', 'R'] as channel, channelIndex}
      <!-- Face, needles and front glass as three stacked copies, so a moving needle repaints only its own layer (see MeterPanel) -->
      {#snippet vuLayer(layer: string)}
      <svg viewBox="0 0 120 72" class="vu layer-{layer}" aria-hidden="true">
        <defs>
          <!-- Warm lamp below the face: brightest at bottom centre, falling off upward and outward -->
          <radialGradient id={gradientId + channel} cx={channelIndex ? '53%' : '47%'} cy="100%" r="95%" fx={channelIndex ? '57%' : '44%'} fy="105%">
            <stop offset="0%" stop-color={lamp[0]} /><stop offset="38%" stop-color={lamp[1]} /><stop offset="75%" stop-color={lamp[2]} /><stop offset="100%" stop-color={lamp[3]} />
          </radialGradient>
          <!-- Bulb shading: the bezel lip shadows the top, corners fall off into the dark, the lamp lights the window's bottom edge -->
          <linearGradient id={gradientId + '-lip'} x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stop-color="#1a0e00" stop-opacity="0.32" /><stop offset="5%" stop-color="#1a0e00" stop-opacity="0.12" /><stop offset="12%" stop-color="#1a0e00" stop-opacity="0.02" /><stop offset="22%" stop-color="#1a0e00" stop-opacity="0" /></linearGradient>
          <radialGradient id={gradientId + channel + '-falloff'} cx={channelIndex ? '56%' : '45%'} cy="92%" r="80%"><stop offset="45%" stop-color="#2a1500" stop-opacity="0" /><stop offset="70%" stop-color="#2a1500" stop-opacity="0.06" /><stop offset="100%" stop-color="#2a1500" stop-opacity="0.26" /></radialGradient>
          <!-- Real bulbs are uneven: a second softer hotspot off to one side and low-frequency mottling, different per meter -->
          <radialGradient id={gradientId + channel + '-blob'} cx={channelIndex ? '28%' : '74%'} cy={channelIndex ? '62%' : '70%'} r="42%"><stop offset="0%" stop-color="#fff2cc" stop-opacity="0.32" /><stop offset="100%" stop-color="#fff2cc" stop-opacity="0" /></radialGradient>
          <filter id={gradientId + channel + '-mottle'} x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency="0.045 0.06" numOctaves="2" seed={channelIndex ? 11 : 4} /><feColorMatrix values="0 0 0 0 0.16  0 0 0 0 0.09  0 0 0 0 0  0 0 0 1.4 -0.55" /></filter>
          <filter id={gradientId + '-inset'} x="-10%" y="-10%" width="120%" height="120%"><feGaussianBlur stdDeviation="2.2" /></filter>
          <filter id={gradientId + '-grain'} x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency={1.6 * textureScale} numOctaves="1" seed={channelIndex ? 3 : 8} /><feColorMatrix values="0 0 0 0 0.2  0 0 0 0 0.13  0 0 0 0 0.05  0 0 0 1.1 -0.45" /></filter>
          <filter id={gradientId + '-soft'} filterUnits="userSpaceOnUse" x="0" y="0" width="120" height="72"><feGaussianBlur stdDeviation="0.7" /></filter>
          <linearGradient id={gradientId + '-sheen'} x1="0" y1="0" x2="1" y2="0.6"><stop offset="0%" stop-color="#fff" stop-opacity="0" /><stop offset="30%" stop-color="#fff" stop-opacity="0.13" /><stop offset="44%" stop-color="#fff" stop-opacity="0.02" /><stop offset="58%" stop-color="#fff" stop-opacity="0" /></linearGradient>
          <!-- Glass: the thick edge bends light unevenly; faint smudges and dust on the pane -->
          <!-- Glass: light in the thick edge is brighter and dimmer along each side (smooth gradients, softened so it reads as light, not print); faint smudges and dust on the pane -->
          <filter id={gradientId + '-edge-soft'} filterUnits="userSpaceOnUse" x="0" y="0" width="120" height="72"><feGaussianBlur stdDeviation="0.35" /></filter>
          <filter id={gradientId + '-seam-soft'} filterUnits="userSpaceOnUse" x="0" y="0" width="120" height="72"><feGaussianBlur stdDeviation="0.15" /></filter>
          <linearGradient id={gradientId + channel + '-edge-patches'} x1={channelIndex ? 1 : 0} y1="0" x2={channelIndex ? 0 : 1} y2="0"><stop offset="0%" stop-color="#fff6de" stop-opacity="0" /><stop offset="12%" stop-color="#fff6de" stop-opacity="0.6" /><stop offset="26%" stop-color="#fff6de" stop-opacity="0.05" /><stop offset="47%" stop-color="#fff6de" stop-opacity="0.45" /><stop offset="63%" stop-color="#fff6de" stop-opacity="0" /><stop offset="84%" stop-color="#fff6de" stop-opacity="0.5" /><stop offset="100%" stop-color="#fff6de" stop-opacity="0.1" /></linearGradient>          <filter id={gradientId + channel + '-dust'} filterUnits="userSpaceOnUse" x="5" y="5" width="110" height="62"><feTurbulence type="fractalNoise" baseFrequency={1.2 * textureScale} numOctaves="1" seed={channelIndex ? 31 : 17} /><feColorMatrix values="0 0 0 0 1  0 0 0 0 0.98  0 0 0 0 0.94  0 0 0 9 -7.4" /></filter>
          <filter id={gradientId + '-smudge'} filterUnits="userSpaceOnUse" x="0" y="0" width="120" height="72"><feGaussianBlur stdDeviation="5" /></filter>
          <linearGradient id={gradientId + '-edge-light'} x1="0" y1="0" x2="1" y2="1"><stop offset="0%" stop-color="#fff6de" stop-opacity="0.9" /><stop offset="35%" stop-color="#fff6de" stop-opacity="0.35" /><stop offset="70%" stop-color="#fff6de" stop-opacity="0.12" /><stop offset="100%" stop-color="#fff6de" stop-opacity="0.4" /></linearGradient>
          <radialGradient id={gradientId + '-shroud'} cx="50%" cy="20%" r="80%"><stop offset="0%" stop-color="#3a352e" /><stop offset="55%" stop-color="#1b1915" /><stop offset="100%" stop-color="#0b0a08" /></radialGradient>
          <linearGradient id={gradientId + '-blade'} x1="0" y1="0" x2="1" y2="0"><stop offset="0%" stop-color="#050505" /><stop offset="55%" stop-color="#141414" /><stop offset="80%" stop-color="#5a5248" /><stop offset="100%" stop-color="#1a1a1a" /></linearGradient>
          <radialGradient id={gradientId + '-screw'} cx="40%" cy="35%" r="70%"><stop offset="0%" stop-color="#d8d2c6" /><stop offset="60%" stop-color="#8a8276" /><stop offset="100%" stop-color="#3c372f" /></radialGradient>
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
        <text x="60" y="53" class="channel" class:note={screwNote}>{screwNote || channel}</text>
        <rect x="5" y="5" width="110" height="62" fill="url(#{gradientId}{channel}-blob)" class="shade" /><rect x="5" y="5" width="110" height="62" filter="url(#{gradientId}{channel}-mottle)" class="shade mottle" />
        <rect x="5" y="5" width="110" height="62" fill="url(#{gradientId}{channel}-falloff)" class="shade" /><rect x="5" y="5" width="110" height="62" fill="url(#{gradientId}-lip)" class="shade" /><g clip-path="url(#{gradientId}-clip)"><rect x="5" y="6.6" width="110" height="62" class="shade inset" filter="url(#{gradientId}-inset)" /></g>
        {#if size !== 'small'}<rect x="5" y="5" width="110" height="62" filter="url(#{gradientId}-grain)" class="shade grain" />
        <g clip-path="url(#{gradientId}-clip)">{#each lamps as lampPosition}{@const [[baseX, baseY], [tipX, tipY]] = castShadow(vuAngle(channelIndex), lampPosition)}<line x1={baseX} y1={baseY} x2={tipX} y2={tipY} class="needle-shadow" filter="url(#{gradientId}-soft)" />{/each}</g>{/if}
        <g clip-path="url(#{gradientId}-clip)"><path d={bladePath} fill="url(#{gradientId}-blade)" class="needle blade" transform="rotate({vuAngle(channelIndex)} {pivotX} {pivotY})" /></g>
        <g class="front">
        <!-- Movement cover: a dark Bakelite dome the needle rises out of, its rim catching the lamp, with the zero-adjust screw -->
        <g clip-path="url(#{gradientId}-clip)"><ellipse cx="60" cy="74" rx="25" ry="16" fill="url(#{gradientId}-shroud)" /><path d="M35.6 74 A24.4 15.4 0 0 1 84.4 74" class="shroud-rim" /><ellipse cx="60" cy="74" rx="25" ry="16" class="shroud-edge" />
          <circle cx="60" cy="64" r="2.6" fill="url(#{gradientId}-screw)" class="screw" /><line x1="57.8" y1="64" x2="62.2" y2="64" class="screw-slot" transform="rotate({screwAngle} 60 64)" /></g>
        {#if size !== 'small'}<circle cx="60" cy="64" r="4.5" class="screw-grip" role="slider" tabindex="-1" aria-label="0 VU" aria-valuenow={meters.reference} onpointerdown={grabScrew} onpointermove={turnScrew} onpointerup={releaseScrew} onpointercancel={releaseScrew} onclick={(event) => event.stopPropagation()} ondblclick={autoScrew}><title>Drag to set 0 VU · double-click for automatic</title></circle>{/if}
        <rect x="7" y="65.6" width="106" height="1" class="lit-edge" />
        <!-- Acrylic window edge: light bends through the thick rim, a bright band with a darker inner seam -->
        <rect x="5.7" y="5.7" width="108.6" height="60.6" rx="1.6" class="rim-glow" /><rect x="7.3" y="7.3" width="105.4" height="57.4" rx="1" class="rim-seam" /><path d="M6 6.2 H114" class="rim-catch" />
        {#if size !== 'small'}<rect x="5" y="5" width="110" height="62" rx="2" fill="url(#{gradientId}-sheen)" class="shade" />
          <g clip-path="url(#{gradientId}-clip)" class="shade">
            <rect x="6.3" y="6.3" width="107.4" height="59.4" rx="1.4" fill="none" stroke="url(#{gradientId}-edge-light)" stroke-width="2.4" filter="url(#{gradientId}-edge-soft)" class="refraction" />
            <rect x="6.3" y="6.3" width="107.4" height="59.4" rx="1.4" fill="none" stroke="url(#{gradientId}{channel}-edge-patches)" stroke-width="2" filter="url(#{gradientId}-edge-soft)" class="refraction patches" />
            <rect x="8.4" y="8.4" width="103.2" height="55.2" rx="1" fill="none" stroke="#3a2508" stroke-width="0.4" filter="url(#{gradientId}-seam-soft)" class="refraction-seam" />
            <ellipse cx={channelIndex ? 88 : 30} cy={channelIndex ? 22 : 48} rx="16" ry="7" transform="rotate({channelIndex ? -18 : 24} {channelIndex ? 88 : 30} {channelIndex ? 22 : 48})" fill="#fff" filter="url(#{gradientId}-smudge)" class="smudge" />
            <rect x="5" y="5" width="110" height="62" filter="url(#{gradientId}{channel}-dust)" class="dust" />
          </g>{/if}
        </g>
      </svg>
      {/snippet}
      <span class="meter-stack">{@render vuLayer('face')}{@render vuLayer('needles')}{@render vuLayer('front')}</span>
    {/each}
  {:else if panelThemes[ui.meterStyle]}
    <MeterPanel theme={ui.meterStyle} levels={meters.vu} reference={meters.reference} {size} {textureScale} idle={!active} />
  {:else if ui.meterStyle === 'oledvu'}
    {#each ['L', 'R'] as channel, channelIndex}
      <!-- Cropped to the scale: the panel holds just the arc, numbers, needle and channel, centred top to bottom -->
      {#snippet oledLayer(layer: string)}
      <svg viewBox="0 17 120 54" class="vu oledvu layer-{layer}" aria-hidden="true">
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
      {/snippet}
      <span class="meter-stack">{@render oledLayer('face')}{@render oledLayer('needles')}</span>
    {/each}
  {:else}
    <!-- Spectrum analyser (the third style; stored as 'oled' so earlier choices carry over) -->
    <div class="spectrum">{#if size !== 'small'}<Analyser levels={meters.bands} labels={bandLabels} scale={spectrumScale} segments={24} title="SPECTRUM ANALYZER" fullRange={Math.max(0, Math.min(1, (Math.max(meters.vu[0] ?? -90, meters.vu[1] ?? -90) + 40) / 40))} />{:else}<Analyser levels={meters.bands} compact={true} segments={12} />{/if}</div>
  {/if}
  {/key}
</button>

<style>
  .meters { display: flex; gap: 4px; background: none; border: 0; padding: 0; cursor: pointer; align-items: center; }
  .small .vu { width: 60px; height: 36px; } .large .vu { width: 190px; height: 114px; }
  /* Whole panels keep their own shape and size: tall round meters, long low monitor strips… */
  .small :global(.panel) { height: 34px; width: auto; max-width: 130px; }
  /* large: as wide as the pair of VU meters, unless that makes a tall panel too tall */
  .large :global(.panel) { width: min(384px, 170px * var(--aspect)); height: auto; }
  .full :global(.panel) { height: auto; width: min(var(--full-share) * 1vw, (100vh - 140px) * var(--aspect)); }
  .full :global(.panel.deck) { --full-share: 78; } .full :global(.panel.orange) { --full-share: 58; } .full :global(.panel.monitor) { --full-share: 94; } .full :global(.panel.blue) { --full-share: 86; } .full :global(.panel.green) { --full-share: 90; }
  .small .oledvu { height: 27px; } .large .oledvu { height: 85.5px; }
  .bezel { fill: #15161a; stroke: #2c2e34; stroke-width: 1; }
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
  .needle { stroke: #111; stroke-width: 0.9; } .needle.blade { stroke: none; } /* ballistics (a damped spring) in the shell, easing and window jog per frame in state.svelte.ts */
  .shroud-rim { fill: none; stroke: #fff3d6; stroke-width: 0.45; opacity: 0.45; transition: opacity 1s; } .idle .shroud-rim { opacity: 0.3; } .shroud-edge { fill: none; stroke: #000; stroke-width: 0.5; opacity: 0.6; }
  .screw { stroke: #1a1712; stroke-width: 0.3; } .screw-grip { fill: transparent; pointer-events: all; cursor: ew-resize; outline: none; } .channel.note { font-size: 4.2px; font-family: "Segoe UI", Arial, sans-serif; fill: rgba(29, 26, 20, 0.75); } .screw-slot { stroke: #26221c; stroke-width: 0.55; stroke-linecap: round; }
  .inset { fill: none; stroke: #1c1206; stroke-width: 4; opacity: 0.55; }
  .percent { stroke-width: 0.5; } .percent-number { fill: #1d1a14; font-size: 3.6px; text-anchor: middle; dominant-baseline: middle; font-family: "Segoe UI", Arial, sans-serif; }
  .pilot { fill: #2b2620; stroke: #7a6646; stroke-width: 0.5; } .pilot-glint { fill: #fff; opacity: 0.45; }
  .shade { pointer-events: none; } .mottle { opacity: 0.07; }
  .rim-glow { fill: none; stroke: #ffe6b4; stroke-width: 1.3; } .rim-seam { fill: none; stroke: #3a2508; stroke-width: 0.45; opacity: 0.35; } .rim-catch { stroke: #fff8e6; stroke-width: 0.5; opacity: 0.6; }
  .grain { opacity: 0.45; } .full .grain { opacity: 0.18; } .needle-shadow { stroke: #2a1a05; stroke-width: 1.1; stroke-linecap: round; opacity: 0.2; }
  .rim-glow { opacity: calc(0.4 * var(--lamp-glow)); } .idle .rim-catch { opacity: 0.3; } .rim-catch, .refraction { transition: opacity 1s; }
  .refraction { opacity: 0.42; } .refraction.patches { opacity: 0.21; } .refraction-seam { opacity: 0.22; } .smudge { opacity: 0.06; } .dust { opacity: 0.22; }
  .idle .refraction { opacity: 0.35; } .idle .refraction.patches { opacity: 0.17; }
  .lit-edge { fill: #ffe7b0; opacity: calc(0.75 * var(--lamp-glow) * var(--lamp-glow)); }
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
  /* Layers: the face hides needles and front glass, the needle layer (its own compositor layer) shows only needles, the front layer only the glass */
  .meter-stack { display: grid; } .meter-stack > svg { grid-area: 1 / 1; }
  .layer-face :is(.needle, .needle-shadow, .oled-needle, .front), .layer-front :is(.needle, .needle-shadow) { display: none; }
  .layer-needles, .layer-front { pointer-events: none; } .layer-needles { will-change: transform; }
  /* display: none, not visibility: hidden: hidden filtered shapes still cost (and drew their noise) in each copy */
  .layer-needles :not(:has(.needle, .needle-shadow, .oled-needle), .needle, .needle-shadow, .oled-needle, defs, defs *), .layer-front :not(:has(.front), .front, .front *, defs, defs *) { display: none; }
  .oledvu.layer-needles { background: none; backdrop-filter: none; box-shadow: none; }
  .oledvu :is(.oled-line, .oled-tick, .oled-number, .oled-label, .oled-needle) { filter: drop-shadow(0 0 1.2px rgba(63, 184, 255, 0.7)); }
</style>
