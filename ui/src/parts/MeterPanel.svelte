<script lang="ts" module>
  let panelCount = 0
  // Whole meter panels after classic decks and amps (style only, no makers' names), each with its own shape, light and mechanics
  export const panelThemes: Record<string, { aspect: number }> = { deck: { aspect: 300 / 96 }, orange: { aspect: 240 / 120 }, monitor: { aspect: 440 / 110 }, blue: { aspect: 360 / 110 }, green: { aspect: 360 / 120 } }
</script>
<script lang="ts">
  let { theme, levels, reference, size, idle }: { theme: string, levels: number[], reference: number, size: string, idle: boolean } = $props()
  const id = `panel-${panelCount++}`
  const height = $derived(theme === 'monitor' || theme === 'blue' ? 110 : theme === 'deck' ? 96 : 120)
  const width = $derived(Math.round(panelThemes[theme].aspect * height))
  // Needle angles: VU faces keep the VU law, power meters are logarithmic (dB re full power spread evenly)
  const vuMarks: [number, number][] = [[-20, -48], [-10, -31], [-7, -21], [-5, -12], [-3, -1], [-2, 6], [-1, 13], [0, 21], [1, 29], [2, 37], [3, 46]]
  const vuBase = (vu: number) => {
    if (vu <= -20) return -54 + 6 * Math.max(0, Math.min(1, (vu + 30) / 10))
    for (let mark = 1; mark < vuMarks.length; mark++) if (vu <= vuMarks[mark][0]) { const [lowDb, lowAngle] = vuMarks[mark - 1], [highDb, highAngle] = vuMarks[mark]; return lowAngle + (highAngle - lowAngle) * (vu - lowDb) / (highDb - lowDb) }
    return Math.min(52, 46 + (vu - 3) * 4)
  }
  const vuAngle = (vu: number, span: number) => (vuBase(vu) - 0) * span / 47 - span * 0 // -48..46 scaled to the face's span
  const powerAngle = (decibels: number, span: number, top = 3) => decibels <= -40 ? -span - 6 + 6 * Math.max(0, Math.min(1, (decibels + 46) / 6)) : Math.min(span + 5, -span + (decibels + 40) / (40 + top) * span * 2)
  const point = (centreX: number, centreY: number, angle: number, radius: number) => [centreX + radius * Math.sin(angle * Math.PI / 180), centreY - radius * Math.cos(angle * Math.PI / 180)]
  const arc = (centreX: number, centreY: number, fromAngle: number, toAngle: number, radius: number) => { const [startX, startY] = point(centreX, centreY, fromAngle, radius); const [endX, endY] = point(centreX, centreY, toAngle, radius); return `M${startX} ${startY} A${radius} ${radius} 0 0 1 ${endX} ${endY}` }
  const relative = (channelIndex: number) => (levels[channelIndex] ?? -90) - reference
  const sum = $derived(10 * Math.log10((Math.pow(10, relative(0) / 10) + Math.pow(10, relative(1) / 10)) / 2 + 1e-12))
  const wattsAt = (wattage: number, full = 100) => 10 * Math.log10(wattage / full)
  const shortWatts = (wattage: number) => wattage < 1 ? String(wattage).replace(/^0/, '') : String(wattage)
  const grainFrequency = $derived(size === 'full' ? 2.4 : 0.9)
  const range = (from: number, to: number, step: number) => Array.from({ length: Math.round((to - from) / step) + 1 }, (unused, index) => from + index * step)
  // Deck
  const deckWindows = [{ x: 12, channel: 'L' }, { x: 170, channel: 'R' }]
  const deckMarks = vuMarks.filter(([decibels]) => decibels !== -2)
  // Orange panel meters
  const orangeMeters = [{ x: 4, channel: 'L' }, { x: 124, channel: 'R' }]
  const orangeMarks: [number, string][] = [[-20, '20'], [-10, '10'], [-7, '7'], [-5, '5'], [-3, '3'], [-1, '1'], [0, '0'], [1, '1'], [3, '3']]
  // Power monitor: three meters in one window, the middle one reads both channels together
  const monitorMeters = $derived([{ centre: 12 + 416 / 6, decibels: relative(0), title: 'POWER MONITOR', channel: 'L' }, { centre: 220, decibels: sum, title: 'L + R', channel: '' }, { centre: 12 + 416 * 5 / 6, decibels: relative(1), title: 'POWER MONITOR', channel: 'R' }])
  const monitorLabels: [number, string][] = [[0.01, '.01'], [0.03, '.03'], [0.1, '.1'], [0.3, '.3'], [1, '1'], [3, '3'], [10, '10'], [30, '30'], [100, '100']]
  // Blue watts and green fluorescent
  const blueWindows = [{ x: 16, channel: 'L' }, { x: 204, channel: 'R' }]
  const blueLabels = [0.01, 0.1, 0.5, 1, 5, 10, 50, 100, 200]
  const greenWindows = [{ x: 8, channel: 'L' }, { x: 202, channel: 'R' }]
  const greenLabels = [0.01, 0.1, 0.5, 1, 5, 10, 50, 100, 200, 500]
</script>

<svg viewBox="0 0 {width} {height}" class="panel {theme}" class:idle aria-hidden="true" style:--aspect={panelThemes[theme].aspect}>
  <defs>
    <filter id={id + '-grain'} x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency={grainFrequency} numOctaves="1" seed="7" /><feColorMatrix values="0 0 0 0 0.2  0 0 0 0 0.15  0 0 0 0 0.08  0 0 0 1.1 -0.5" /></filter>
    <filter id={id + '-brushed'} x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency="0.004 0.9" numOctaves="2" seed="3" /><feColorMatrix values="0 0 0 0 0.5  0 0 0 0 0.5  0 0 0 0 0.52  0 0 0 0.6 0" /></filter>
    <filter id={id + '-soft'} filterUnits="userSpaceOnUse" x="0" y="0" width={width} height={height}><feGaussianBlur stdDeviation="1" /></filter>
    <filter id={id + '-bloom'} x="-40%" y="-40%" width="180%" height="180%"><feGaussianBlur stdDeviation="4" /></filter>
    <filter id={id + '-glow'} filterUnits="userSpaceOnUse" x="0" y="0" width={width} height={height}><feGaussianBlur stdDeviation="0.9" result="blur" /><feMerge><feMergeNode in="blur" /><feMergeNode in="blur" /><feMergeNode in="SourceGraphic" /></feMerge></filter>
    <linearGradient id={id + '-sheen'} x1="0" y1="0" x2="1" y2="0.7"><stop offset="0%" stop-color="#fff" stop-opacity="0" /><stop offset="28%" stop-color="#fff" stop-opacity="0.12" /><stop offset="40%" stop-color="#fff" stop-opacity="0.03" /><stop offset="55%" stop-color="#fff" stop-opacity="0" /></linearGradient>
    <linearGradient id={id + '-silver'} x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stop-color="#d9dcdf" /><stop offset="50%" stop-color="#c2c5c9" /><stop offset="100%" stop-color="#a9adb2" /></linearGradient>
    <radialGradient id={id + '-chrome'} cx="40%" cy="35%" r="70%"><stop offset="0%" stop-color="#ffffff" /><stop offset="45%" stop-color="#b8bcc2" /><stop offset="100%" stop-color="#4b4f55" /></radialGradient>
    <radialGradient id={id + '-brass'} cx="45%" cy="25%" r="80%"><stop offset="0%" stop-color="#ffe7a8" /><stop offset="55%" stop-color="#a5782c" /><stop offset="100%" stop-color="#3b2a0c" /></radialGradient>
  </defs>

  {#if theme === 'deck'}
    <!-- Tape deck: brushed silver front, two recessed windows lit by a lamp along their top edge, brass pivot caps, peak lamp between -->
    <defs>
      <linearGradient id={id + '-deckface'} x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stop-color="#fff4da" /><stop offset="35%" stop-color="#f2e8d2" /><stop offset="100%" stop-color="#cfc4ab" /></linearGradient>
      {#each deckWindows as deck}<clipPath id="{id}-deck-{deck.channel}"><rect x={deck.x + 4} y="14" width="110" height="68" rx="1.5" /></clipPath>{/each}
    </defs>
    <rect width={width} height={height} rx="4" fill="url(#{id}-silver)" /><rect width={width} height={height} rx="4" filter="url(#{id}-brushed)" opacity="0.5" />
    {#each deckWindows as deck, channelIndex}
      {@const centreX = deck.x + 59}{@const pivotY = 118}{@const angle = vuAngle(relative(channelIndex), 34)}
      <rect x={deck.x} y="10" width="118" height="76" rx="3" fill="#6d7075" /><rect x={deck.x + 0.5} y="10.5" width="117" height="75" rx="3" fill="none" stroke="#f4f6f8" stroke-width="0.8" opacity="0.7" />
      <rect x={deck.x + 4} y="14" width="110" height="68" rx="1.5" fill="url(#{id}-deckface)" class="face" />
      <g clip-path="url(#{id}-deck-{deck.channel})">
        <rect x={deck.x + 8} y="13" width="102" height="5" fill="#fff3cf" filter="url(#{id}-bloom)" class="lamp" /><rect x={deck.x + 10} y="14.6" width="98" height="1.4" fill="#fffaf0" class="lamp" />
        <path d={arc(centreX, pivotY, -34, vuAngle(0, 34), 80)} class="ink-line" /><path d={arc(centreX, pivotY, vuAngle(0, 34), 33, 80)} class="red-line" /><path d={arc(centreX, pivotY, vuAngle(0, 34), 33, 82.5)} class="red-band thin" />
        {#each deckMarks as [decibels]}{@const markAngle = vuAngle(decibels, 34)}{@const [innerX, innerY] = point(centreX, pivotY, markAngle, 80)}{@const [outerX, outerY] = point(centreX, pivotY, markAngle, 84.5)}{@const [labelX, labelY] = point(centreX, pivotY, markAngle, 89.5)}
          <line x1={innerX} y1={innerY} x2={outerX} y2={outerY} class="ink-tick" class:red={decibels > 0} /><text x={labelX} y={labelY} class="deck-number" class:red={decibels > 0}>{Math.abs(decibels)}</text>{/each}
        {#each [[0, -20], [30, -10.46], [50, -6.02], [70, -3.1], [100, 0]] as [percent, decibels]}{@const markAngle = vuAngle(decibels, 34)}{@const [tickX, tickY] = point(centreX, pivotY, markAngle, 78)}{@const [textX, textY] = point(centreX, pivotY, markAngle, 74)}<circle cx={tickX} cy={tickY} r={percent === 100 ? 1.1 : 0.6} class:red={percent === 100} class="ink-dot" /><text x={textX} y={textY} class="deck-small">{percent}</text>{/each}
        <text x={deck.x + 24} y="52" class="deck-small">%</text><text x={deck.x + 12} y="56" class="deck-sign">−</text><text x={deck.x + 106} y="56" class="deck-sign red">+</text>
        <text x={centreX} y="64" class="deck-vu">VU</text><text x={centreX} y="73" class="deck-channel">{deck.channel}</text>
        <rect x={deck.x + 4} y="14" width="110" height="68" filter="url(#{id}-grain)" class="grain" />
        <line x1={centreX + 1.6} y1={pivotY} x2={centreX + 1.6} y2={pivotY - 86} class="needle-shadow" filter="url(#{id}-soft)" transform="rotate({angle} {centreX} {pivotY - 2})" />
        <line x1={centreX} y1={pivotY} x2={centreX} y2={pivotY - 86} class="needle dark" transform="rotate({angle} {centreX} {pivotY})" />
        <ellipse cx={centreX} cy="82" rx="13" ry="5.5" fill="url(#{id}-brass)" /><rect x={deck.x + 4} y="14" width="110" height="68" fill="none" stroke="#000" stroke-width="3" opacity="0.18" filter="url(#{id}-soft)" />
      </g>
    {/each}
    <text x="150" y="20" class="panel-label">PEAK</text><text x="150" y="26" class="panel-label">CHECK</text>
    <circle cx="150" cy="40" r="9" fill="url(#{id}-chrome)" stroke="#55595f" stroke-width="0.6" /><circle cx="150" cy="40" r="4" fill="none" stroke="#fff" stroke-width="0.4" opacity="0.5" />
    <circle cx="150" cy="70" r="2.4" class="led" /><circle cx="150" cy="70" r="5" class="led-glow" filter="url(#{id}-bloom)" />
    

  {:else if theme === 'orange'}
    <!-- Panel meters with an arched window, the whole face glowing orange and the movement on show: bridge, coil and counterweighted needle -->
    <defs>
      <radialGradient id={id + '-orange'} cx="50%" cy="72%" r="75%"><stop offset="0%" stop-color="#fff0b0" /><stop offset="30%" stop-color="#ffc24a" /><stop offset="70%" stop-color="#f08a14" /><stop offset="100%" stop-color="#a14706" /></radialGradient>
      {#each orangeMeters as meter}<clipPath id="{id}-orange-{meter.channel}"><path d="M{meter.x + 12} {106} V{50} A44 44 0 0 1 {meter.x + 100} {50} V{106} Z" /></clipPath>{/each}
    </defs>
    {#each orangeMeters as meter, channelIndex}
      {@const centreX = meter.x + 56}{@const pivotY = 88}{@const angle = vuAngle(relative(channelIndex), 42)}{@const facePath = `M${meter.x + 12} 106 V50 A44 44 0 0 1 ${meter.x + 100} 50 V106 Z`}
      <path d={facePath} fill="#ff9a1a" filter="url(#{id}-bloom)" class="spill" />
      <rect x={meter.x} y="2" width="112" height="116" rx="12" fill="#151312" stroke="#34302c" stroke-width="1" />
      {#each [[meter.x + 8, 10], [meter.x + 104, 10], [meter.x + 8, 110], [meter.x + 104, 110]] as [screwX, screwY]}<circle cx={screwX} cy={screwY} r="2.2" fill="#2a2724" stroke="#000" stroke-width="0.5" /><line x1={screwX - 1.4} y1={screwY} x2={screwX + 1.4} y2={screwY} stroke="#000" stroke-width="0.5" />{/each}
      <path d={facePath} fill="url(#{id}-orange)" class="face" />
      <g clip-path="url(#{id}-orange-{meter.channel})">
        <path d={arc(centreX, pivotY, -42, vuAngle(0, 42), 48)} class="ink-line" /><path d={arc(centreX, pivotY, vuAngle(0, 42), 41, 48)} class="red-line" /><path d={arc(centreX, pivotY, vuAngle(0, 42), 41, 50.5)} class="red-band" />
        {#each orangeMarks as [decibels, text]}{@const markAngle = vuAngle(decibels, 42)}{@const [innerX, innerY] = point(centreX, pivotY, markAngle, 48)}{@const [outerX, outerY] = point(centreX, pivotY, markAngle, 52.5)}{@const [labelX, labelY] = point(centreX, pivotY, markAngle, 58)}
          <line x1={innerX} y1={innerY} x2={outerX} y2={outerY} class="ink-tick" class:red={decibels > 0} /><text x={labelX} y={labelY} class="orange-number" class:red={decibels > 0}>{text}</text>{/each}
        {#each [[30, -10.46], [50, -6.02], [70, -3.1], [100, 0]] as [percent, decibels]}{@const markAngle = vuAngle(decibels, 42)}{@const [textX, textY] = point(centreX, pivotY, markAngle, 42.5)}<text x={textX} y={textY} class="orange-small">{percent}</text>{/each}
        <text x={centreX - 30} y="62" class="orange-small">%</text><text x={centreX + 38} y="56" class="orange-sign">+</text><text x={centreX} y="70" class="orange-vu">VU</text>
        <rect x={meter.x + 12} y="6" width="88" height="100" filter="url(#{id}-grain)" class="grain" />
        <line x1={centreX + 1.6} y1={pivotY} x2={centreX + 1.6} y2={pivotY - 58} class="needle-shadow" filter="url(#{id}-soft)" transform="rotate({angle} {centreX} {pivotY - 2})" />
        <!-- the movement: bridge plate with screws, copper coil, hub and the needle's counterweight -->
        <path d="M{centreX - 30} 106 V94 Q{centreX - 30} 88 {centreX - 22} 87 H{centreX + 22} Q{centreX + 30} 88 {centreX + 30} 94 V106 Z" class="bridge" />
        <circle cx={centreX - 21} cy="96" r="2.6" class="screw" /><circle cx={centreX + 21} cy="96" r="2.6" class="screw" /><line x1={centreX - 22.6} y1="96" x2={centreX - 19.4} y2="96" class="screw-slot" /><line x1={centreX + 19.4} y1="96" x2={centreX + 22.6} y2="96" class="screw-slot" />
        <rect x={centreX - 9} y="91" width="18" height="10" rx="2" class="coil" />
        <g transform="rotate({angle} {centreX} {pivotY})"><line x1={centreX} y1={pivotY + 9} x2={centreX} y2={pivotY - 58} class="needle orange-needle" /><circle cx={centreX} cy={pivotY + 9} r="2" class="counterweight" /></g>
        <circle cx={centreX} cy={pivotY} r="3.4" class="hub" /><circle cx={centreX - 0.8} cy={pivotY - 0.8} r="1" fill="#fff" opacity="0.35" />
        <path d={facePath} fill="url(#{id}-sheen)" />
      </g>
      <path d={facePath} fill="none" stroke="#000" stroke-width="1.2" opacity="0.6" />
    {/each}

  {:else if theme === 'monitor'}
    <!-- Power monitor: one long window holding three meters, cool white light rising from below, triangular markers, silver surround -->
    <defs>
      <linearGradient id={id + '-monitorface'} x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stop-color="#c9cec3" /><stop offset="55%" stop-color="#e3e7dc" /><stop offset="88%" stop-color="#f6f8f0" /><stop offset="100%" stop-color="#ffffff" /></linearGradient>
      <clipPath id={id + '-monitor'}><rect x="12" y="12" width="416" height="70" /></clipPath>
    </defs>
    <rect width={width} height={height} rx="8" fill="url(#{id}-silver)" /><rect width={width} height={height} rx="8" filter="url(#{id}-brushed)" opacity="0.45" />
    <rect x="8" y="8" width="424" height="78" rx="2" fill="#101112" />
    <rect x="12" y="12" width="416" height="70" fill="url(#{id}-monitorface)" class="face" />
    <g clip-path="url(#{id}-monitor)">
      <rect x="20" y="74" width="400" height="10" fill="#ffffff" filter="url(#{id}-bloom)" class="lamp" />
      <line x1={12 + 416 / 3} y1="14" x2={12 + 416 / 3} y2="80" class="divider" /><line x1={12 + 416 * 2 / 3} y1="14" x2={12 + 416 * 2 / 3} y2="80" class="divider" />
      {#each monitorMeters as meter}
        {@const pivotY = 132}{@const angle = powerAngle(meter.decibels, 34)}
        <path d={arc(meter.centre, pivotY, -34, 34, 100)} class="ink-line thin" /><path d={arc(meter.centre, pivotY, powerAngle(0, 34), 34, 101.2)} class="red-band thin" />
        {#each monitorLabels as [wattage, text]}{@const markAngle = powerAngle(wattsAt(wattage), 34)}{@const [tipX, tipY] = point(meter.centre, pivotY, markAngle, 100.5)}{@const [leftX, leftY] = point(meter.centre, pivotY, markAngle - 1, 104.5)}{@const [rightX, rightY] = point(meter.centre, pivotY, markAngle + 1, 104.5)}{@const [labelX, labelY] = point(meter.centre, pivotY, markAngle, 108.5)}
          <path d="M{tipX} {tipY} L{leftX} {leftY} L{rightX} {rightY} Z" class="marker" /><text x={labelX} y={labelY} class="monitor-number">{text}</text>{/each}
        {@const [clipX, clipY] = point(meter.centre, pivotY, 33, 95)}<text x={clipX} y={clipY} class="monitor-number red">CLIP</text>
        {#each range(-40, 3, 2.5) as decibels}{@const markAngle = powerAngle(decibels, 34)}{@const [innerX, innerY] = point(meter.centre, pivotY, markAngle, 100)}{@const [outerX, outerY] = point(meter.centre, pivotY, markAngle, 98.4)}<line x1={innerX} y1={innerY} x2={outerX} y2={outerY} class="ink-tick fine" />{/each}
        <text x={meter.centre} y="48" class="monitor-title">{meter.title}</text>{#if meter.channel}<text x={meter.centre} y="56" class="monitor-channel">{meter.channel}</text>{/if}
        <line x1={meter.centre + 1.8} y1={pivotY} x2={meter.centre + 1.8} y2={pivotY - 106} class="needle-shadow" filter="url(#{id}-soft)" transform="rotate({angle} {meter.centre} {pivotY - 2})" />
        <line x1={meter.centre} y1={pivotY} x2={meter.centre} y2={pivotY - 106} class="needle dark" transform="rotate({angle} {meter.centre} {pivotY})" />
      {/each}
      <rect x="12" y="12" width="416" height="70" filter="url(#{id}-grain)" class="grain" />
      <rect x="12" y="12" width="416" height="70" fill="none" stroke="#000" stroke-width="4" opacity="0.28" filter="url(#{id}-soft)" />
      <rect x="12" y="12" width="416" height="70" fill="url(#{id}-sheen)" />
    </g>
    <rect x="8" y="90" width="424" height="12" rx="1" fill="#d5d8db" /><rect x="8" y="90" width="424" height="0.8" fill="#fff" opacity="0.8" />

  {:else if theme === 'blue'}
    <!-- Big power amp: one black glass window, two deep blue faces glowing from the middle, white scales, red needles, a dim display between -->
    <defs>
      <radialGradient id={id + '-blue'} cx="50%" cy="78%" r="72%"><stop offset="0%" stop-color="#6aa2ff" /><stop offset="25%" stop-color="#2f5fe0" /><stop offset="60%" stop-color="#10246e" /><stop offset="100%" stop-color="#03061a" /></radialGradient>
      {#each blueWindows as blue}<clipPath id="{id}-blue-{blue.channel}"><rect x={blue.x} y="12" width="140" height="78" rx="2" /></clipPath>{/each}
    </defs>
    <rect width={width} height={height} rx="4" fill="#06070a" /><rect x="6" y="6" width={width - 12} height={height - 12} rx="2" fill="#020305" stroke="#1c1f26" stroke-width="0.8" />
    {#each blueWindows as blue, channelIndex}
      {@const centreX = blue.x + 70}{@const pivotY = 96}{@const angle = powerAngle(relative(channelIndex), 52)}
      <rect x={blue.x} y="12" width="140" height="78" rx="2" fill="url(#{id}-blue)" class="face" />
      <g clip-path="url(#{id}-blue-{blue.channel})">
        <ellipse cx={centreX} cy="74" rx="44" ry="22" fill="#7fb0ff" opacity="0.35" filter="url(#{id}-bloom)" class="lamp" />
        <g filter="url(#{id}-glow)" class="blue-ink">
          <path d={arc(centreX, pivotY, -52, powerAngle(0, 52), 62)} class="white-line" /><path d={arc(centreX, pivotY, powerAngle(0, 52), 52, 62)} class="red-line" /><path d={arc(centreX, pivotY, powerAngle(0, 52), 52, 64.5)} class="red-band" />
          {#each range(-40, 3, 1) as decibels}{@const markAngle = powerAngle(decibels, 52)}{@const [innerX, innerY] = point(centreX, pivotY, markAngle, 62)}{@const [outerX, outerY] = point(centreX, pivotY, markAngle, decibels % 10 === 0 ? 67 : 64.5)}<line x1={innerX} y1={innerY} x2={outerX} y2={outerY} class="white-tick" class:red={decibels > 0} />{/each}
          {#each blueLabels as wattage}{@const markAngle = powerAngle(wattsAt(wattage), 52)}{@const [labelX, labelY] = point(centreX, pivotY, markAngle, 72)}<text x={labelX} y={labelY} class="blue-number" class:red={wattage > 100}>{shortWatts(wattage)}</text>{/each}
          {#each [-40, -30, -20, -10, -3, 0, 3] as decibels}{@const markAngle = powerAngle(decibels, 52)}{@const [textX, textY] = point(centreX, pivotY, markAngle, 55)}<text x={textX} y={textY} class="blue-small">{decibels > 0 ? '+' + decibels : decibels}</text>{/each}
          <text x={centreX} y="62" class="blue-title">WATTS · {blue.channel}</text><text x={centreX} y="69" class="blue-small dim">dB</text>
        </g>
        <rect x={blue.x} y="12" width="140" height="78" filter="url(#{id}-grain)" class="grain dark-grain" />
        <line x1={centreX} y1={pivotY} x2={centreX} y2={pivotY - 74} class="needle red-needle" transform="rotate({angle} {centreX} {pivotY})" filter="url(#{id}-glow)" />
      </g>
    {/each}
    <rect x="165" y="44" width="30" height="12" rx="1.5" fill="#05070c" stroke="#1e2430" stroke-width="0.5" /><text x="180" y="51.5" class="display-text">STANDBY</text>
    <rect x="6" y="6" width={width - 12} height={height - 12} rx="2" fill="url(#{id}-sheen)" />

  {:else if theme === 'green'}
    <!-- Smoked glass front, two wide fluorescent meters: a fan of green bars, watts above and dB below, a black centre panel with little lit legends -->
    <defs>
      <radialGradient id={id + '-green'} cx="50%" cy="80%" r="75%"><stop offset="0%" stop-color="#1e4a2c" /><stop offset="45%" stop-color="#0d2416" /><stop offset="100%" stop-color="#030806" /></radialGradient>
      {#each greenWindows as green}<clipPath id="{id}-green-{green.channel}"><rect x={green.x} y="8" width="150" height="100" rx="2" /></clipPath>{/each}
    </defs>
    <rect width={width} height={height} rx="4" fill="#0b0d0c" />
    {#each greenWindows as green, channelIndex}
      {@const centreX = green.x + 75}{@const pivotY = 112}{@const angle = powerAngle(relative(channelIndex), 55, 7)}
      <rect x={green.x} y="8" width="150" height="100" rx="2" fill="url(#{id}-green)" class="face" />
      <g clip-path="url(#{id}-green-{green.channel})">
        <g filter="url(#{id}-glow)" class="green-ink">
          <text x={centreX} y="22" class="green-title">WATTS 8Ω</text>
          {#each range(-40, 7, 1) as decibels}{@const markAngle = powerAngle(decibels, 55, 7)}{@const tall = Math.abs(decibels % 10) === 0}{@const [innerX, innerY] = point(centreX, pivotY, markAngle, 64)}{@const [outerX, outerY] = point(centreX, pivotY, markAngle, tall ? 76 : 72)}<line x1={innerX} y1={innerY} x2={outerX} y2={outerY} class="bar" class:tall class:hot={decibels > 0} />{/each}
          <path d={arc(centreX, pivotY, -55, 55, 62)} class="green-line" />
          {#each greenLabels as wattage}{@const markAngle = powerAngle(wattsAt(wattage), 55, 7)}{@const [labelX, labelY] = point(centreX, pivotY, markAngle, 82)}<text x={labelX} y={labelY} class="green-number">{shortWatts(wattage)}</text>{/each}
          {#each [-40, -30, -20, -10, -5, 0, 3, 6] as decibels}{@const markAngle = powerAngle(decibels, 55, 7)}{@const [textX, textY] = point(centreX, pivotY, markAngle, 55)}<text x={textX} y={textY} class="green-small">{decibels > 0 ? '+' + decibels : decibels}</text>{/each}
          <text x={centreX} y="84" class="green-title">POWER</text><text x={centreX} y="92" class="green-small dim">dB · {green.channel}</text>
        </g>
        <line x1={centreX} y1={pivotY} x2={centreX} y2={pivotY - 84} class="needle green-needle" transform="rotate({angle} {centreX} {pivotY})" filter="url(#{id}-glow)" />
        <rect x={green.x} y="8" width="150" height="100" filter="url(#{id}-grain)" class="grain dark-grain" />
      </g>
      <rect x={green.x} y="8" width="150" height="100" rx="2" fill="none" stroke="#2c3a31" stroke-width="0.8" />
    {/each}
    <rect x="162" y="8" width="36" height="100" fill="#060707" />
    <g filter="url(#{id}-glow)">{#each [[30, 'PHASE'], [56, 'CAPACITOR'], [82, 'DUAL MONO']] as [lineY, text]}<text x="166" y={Number(lineY) - 3} class="legend">{text}</text><rect x="178" y={lineY} width="16" height="1.4" class="legend-bar" />{/each}</g>
    <path d="M0 0 L{width * 0.42} 0 L{width * 0.2} {height} L0 {height} Z" fill="#fff" opacity="0.05" /><rect width={width} height={height} rx="4" fill="url(#{id}-sheen)" />
  {/if}
</svg>

<style>
  .panel { display: block; }
  .face { stroke: rgba(0, 0, 0, 0.4); stroke-width: 0.5; }
  .idle .face, .idle .lamp, .idle .spill { opacity: 0.7; } .idle .lamp { opacity: 0.45; }
  .idle :is(.blue-ink, .green-ink) { opacity: 0.6; }
  .grain { opacity: 0.35; pointer-events: none; } .dark-grain { opacity: 0.18; }
  .ink-line { fill: none; stroke: #1e1b16; stroke-width: 0.9; stroke-linecap: round; } .ink-line.thin { stroke-width: 0.5; }
  .red-line { fill: none; stroke: #c4291c; stroke-width: 0.9; } .red-band { fill: none; stroke: #c4291c; stroke-width: 3; } .red-band.thin { stroke-width: 1.6; }
  .ink-tick { stroke: #1e1b16; stroke-width: 0.9; stroke-linecap: round; } .ink-tick.fine { stroke-width: 0.4; } .ink-tick.red { stroke: #c4291c; }
  .ink-dot { fill: #1e1b16; } .ink-dot.red { fill: #c4291c; }
  text { font-family: "Segoe UI", Arial, sans-serif; text-anchor: middle; dominant-baseline: middle; }
  .red { fill: #c4291c; }
  .deck-number { font-size: 6.5px; font-weight: 600; fill: #1e1b16; } .deck-number.red { fill: #c4291c; }
  .deck-small { font-size: 4.2px; fill: #1e1b16; } .deck-sign { font-size: 8px; font-weight: 700; fill: #1e1b16; } .deck-sign.red { fill: #c4291c; }
  .deck-vu { font-size: 9px; font-weight: 700; fill: #1e1b16; letter-spacing: 0.5px; } .deck-channel { font-size: 4.5px; fill: rgba(30, 27, 22, 0.55); letter-spacing: 1px; }
  .panel-label { font-size: 4.6px; font-weight: 700; fill: #2a2c30; letter-spacing: 0.4px; }
  .led { fill: #45ff6a; } .led-glow { fill: #45ff6a; opacity: 0.6; } .idle .led-glow { opacity: 0.25; }
  .orange-number { font-size: 7px; font-weight: 600; fill: #1c0f03; } .orange-number.red { fill: #9e1208; }
  .orange-small { font-size: 4.6px; fill: #1c0f03; } .orange-sign { font-size: 8px; font-weight: 700; fill: #9e1208; } .orange-vu { font-size: 10px; font-weight: 700; fill: #1c0f03; }
  .spill { opacity: 0.55; }
  .bridge { fill: #2a1d0e; stroke: #0f0a04; stroke-width: 0.6; } .screw { fill: #8a7a62; stroke: #2b2216; stroke-width: 0.5; } .screw-slot { stroke: #2b2216; stroke-width: 0.6; }
  .coil { fill: #9c5a22; stroke: #4a2a0c; stroke-width: 0.5; } .hub { fill: #1a120a; stroke: #b38a52; stroke-width: 0.7; } .counterweight { fill: #1b0f04; }
  .needle { stroke-width: 0.9; stroke-linecap: round; } .needle.dark { stroke: #141210; } .orange-needle { stroke: #1b0f04; stroke-width: 1.1; }
  .needle-shadow { stroke: #000; stroke-width: 1.5; opacity: 0.2; }
  .divider { stroke: #8d9288; stroke-width: 0.6; }
  .marker { fill: #2b2d2a; } .monitor-number { font-size: 4.6px; fill: #2b2d2a; } .monitor-number.red { fill: #c8341a; font-weight: 700; font-size: 4px; }
  .monitor-title { font-size: 4.2px; letter-spacing: 1.2px; fill: #2b2d2a; } .monitor-channel { font-size: 3.6px; fill: #6a6e66; letter-spacing: 1px; }
  .white-line { fill: none; stroke: #eef4ff; stroke-width: 0.9; } .white-tick { stroke: #eef4ff; stroke-width: 0.6; } .white-tick.red { stroke: #ff5148; }
  .blue-number { font-size: 5.6px; fill: #f2f6ff; font-weight: 600; } .blue-number.red { fill: #ff5148; }
  .blue-small { font-size: 3.8px; fill: #a9c4ff; } .blue-small.dim { fill: #7f9ad8; } .blue-title { font-size: 4.4px; fill: #dfe9ff; letter-spacing: 1px; }
  .red-needle { stroke: #ff3b30; stroke-width: 1; }
  .display-text { font-size: 3.6px; fill: #2c5bd8; letter-spacing: 0.6px; opacity: 0.8; }
  .bar { stroke: #4dff86; stroke-width: 1.2; } .bar.tall { stroke-width: 1.5; } .bar.hot { stroke: #c4ff6a; }
  .green-line { fill: none; stroke: #4dff86; stroke-width: 0.7; }
  .green-number { font-size: 6px; fill: #6dff9a; font-weight: 600; } .green-small { font-size: 4px; fill: #57e884; } .green-small.dim { opacity: 0.7; }
  .green-title { font-size: 5px; fill: #6dff9a; letter-spacing: 1px; font-weight: 600; }
  .green-needle { stroke: #cfffdc; stroke-width: 0.8; }
  .legend { font-size: 3px; fill: #b6c2b9; text-anchor: start; letter-spacing: 0.3px; } .legend-bar { fill: #d7ff4a; }
</style>
