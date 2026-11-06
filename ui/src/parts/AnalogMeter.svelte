<script lang="ts" module>
  let analogCount = 0
  // Meter faces after classic amps and decks (style only, no makers' names): lamp colours, inks, scale, bezel and the bits around the needle
  type Theme = { face: [string, string, string, string], lamp: [string, string], ink: string, hot: string, needle: string, glow?: string, scale: 'vu' | 'watts' | 'monitor', top?: string, bottom?: string, bezel: string, bezelEdge: string, pivot?: 'mechanism' | 'dome', arch?: boolean, topLamp?: boolean, frameGlow?: string, segments?: boolean, decibels?: boolean, percent?: boolean, markers?: boolean, smoked?: boolean }
  export const analogThemes: Record<string, Theme> = {
    deck: { face: ['#fbf3df', '#f1e5c9', '#ddcaa5', '#b8a07a'], lamp: ['50%', '-10%'], ink: '#24211b', hot: '#c4291c', needle: '#151311', scale: 'vu', percent: true, bezel: '#b9bcc0', bezelEdge: '#e9ebee', pivot: 'dome', topLamp: true },
    orange: { face: ['#ffe2a0', '#ffb43c', '#e9801a', '#9a4708'], lamp: ['50%', '96%'], ink: '#221204', hot: '#9e1208', needle: '#1b0f04', scale: 'vu', percent: true, bezel: '#121110', bezelEdge: '#2e2b28', pivot: 'mechanism', arch: true },
    monitor: { face: ['#f7f9f1', '#e9eee2', '#d3dacb', '#aab4a3'], lamp: ['50%', '102%'], ink: '#2b2d2a', hot: '#d03a1c', needle: '#121212', scale: 'monitor', markers: true, bottom: 'POWER MONITOR', bezel: '#c3c6c9', bezelEdge: '#eef0f2' },
    blue: { face: ['#4d8cff', '#1f45c2', '#0c1d63', '#040920'], lamp: ['50%', '78%'], ink: '#e9f1ff', hot: '#ff5148', needle: '#ff3b30', glow: '#8fb6ff', scale: 'watts', top: 'WATTS', bottom: 'POWER', bezel: '#0c0d10', bezelEdge: '#2a2d33' },
    green: { face: ['#1f3027', '#121c17', '#0a110d', '#040705'], lamp: ['50%', '85%'], ink: '#5dff8f', hot: '#b8ff6a', needle: '#c8ffd9', glow: '#3dff7a', scale: 'watts', segments: true, decibels: true, top: 'WATTS 8Ω', bottom: 'POWER', bezel: '#101211', bezelEdge: '#2b302d', smoked: true },
    amber: { face: ['#1e1c14', '#14130e', '#0c0b08', '#060504'], lamp: ['50%', '90%'], ink: '#74ff7f', hot: '#74ff7f', needle: '#d6ffd9', glow: '#46ff5a', scale: 'watts', segments: true, decibels: true, top: 'WATTS/8Ω', bottom: 'PEAK LEVEL/dB', bezel: '#0d0d0d', bezelEdge: '#2a2a2a', frameGlow: '#ffb21a', smoked: true }
  }
</script>
<script lang="ts">
  let { theme, level, reference, channel, channelIndex, size, idle }: { theme: string, level: number, reference: number, channel: string, channelIndex: number, size: string, idle: boolean } = $props()
  const look = $derived(analogThemes[theme])
  const meterId = `analog-${analogCount++}`
  const pivotX = 60, pivotY = 88, restAngle = -54
  const vuMarks: [number, number][] = [[-20, -48], [-10, -31], [-7, -21], [-5, -12], [-3, -1], [-2, 6], [-1, 13], [0, 21], [1, 29], [2, 37], [3, 46]]
  // VU faces use the classic VU spacing; power meters are logarithmic: -40..+3 dB re 100 W spread evenly
  const vuAngle = (vu: number) => {
    if (vu <= -20) return restAngle + (-48 - restAngle) * Math.max(0, Math.min(1, (vu + 30) / 10))
    for (let mark = 1; mark < vuMarks.length; mark++) if (vu <= vuMarks[mark][0]) { const [lowDb, lowAngle] = vuMarks[mark - 1], [highDb, highAngle] = vuMarks[mark]; return lowAngle + (highAngle - lowAngle) * (vu - lowDb) / (highDb - lowDb) }
    return Math.min(52, 46 + (vu - 3) * 4)
  }
  const powerAngle = (decibels: number) => decibels <= -40 ? restAngle + (-48 - restAngle) * Math.max(0, Math.min(1, (decibels + 46) / 6)) : Math.min(52, -48 + (decibels + 40) / 43 * 94)
  const angleFor = (decibels: number) => look.scale === 'vu' ? vuAngle(decibels) : powerAngle(decibels)
  const needle = $derived(angleFor(level - reference))
  const point = (angle: number, radius: number) => [pivotX + radius * Math.sin(angle * Math.PI / 180), pivotY - radius * Math.cos(angle * Math.PI / 180)]
  const arc = (fromAngle: number, toAngle: number, radius: number) => { const [startX, startY] = point(fromAngle, radius); const [endX, endY] = point(toAngle, radius); return `M${startX} ${startY} A${radius} ${radius} 0 0 1 ${endX} ${endY}` }
  const watts = [0.01, 0.1, 0.5, 1, 5, 10, 50, 100, 200].map(wattage => [wattage, 10 * Math.log10(wattage / 100)] as [number, number])
  const marks = $derived(look.scale === 'vu' ? vuMarks.map(([decibels]) => [Math.abs(decibels), decibels] as [number, number]) : watts)
  const percentMarks: [number, number][] = [[20, -13.98], [40, -7.96], [60, -4.44], [80, -1.94], [100, 0]]
  const decibelMarks = [-40, -30, -20, -10, -5, 0, 3]
  const facePath = $derived(look.arch ? 'M5 67 V34 Q5 5 60 5 Q115 5 115 34 V67 Z' : 'M7 5 H113 Q115 5 115 7 V65 Q115 67 113 67 H7 Q5 67 5 65 V7 Q5 5 7 5 Z')
  const bezelPath = $derived(look.arch ? 'M0 72 V32 Q0 0 60 0 Q120 0 120 32 V72 Z' : 'M5 0 H115 Q120 0 120 5 V67 Q120 72 115 72 H5 Q0 72 0 67 V5 Q0 0 5 0 Z')
  const label = (wattage: number) => wattage < 1 ? String(wattage).replace(/^0/, '') : String(wattage)
</script>

<svg viewBox="0 0 120 72" class="vu analog {theme}" class:idle aria-hidden="true" style:--ink={look.ink} style:--hot={look.hot} style:--glow={look.glow ?? 'transparent'}>
  <defs>
    <radialGradient id={meterId + '-lamp'} cx={look.lamp[0]} cy={look.lamp[1]} r="100%" fx={channelIndex ? '54%' : '46%'} fy={look.lamp[1]}>
      <stop offset="0%" stop-color={look.face[0]} /><stop offset="35%" stop-color={look.face[1]} /><stop offset="72%" stop-color={look.face[2]} /><stop offset="100%" stop-color={look.face[3]} />
    </radialGradient>
    <linearGradient id={meterId + '-bezel'} x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stop-color={look.bezelEdge} /><stop offset="18%" stop-color={look.bezel} /><stop offset="100%" stop-color={look.bezel} /></linearGradient>
    <linearGradient id={meterId + '-lip'} x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stop-color="#000" stop-opacity="0.38" /><stop offset="6%" stop-color="#000" stop-opacity="0.12" /><stop offset="14%" stop-color="#000" stop-opacity="0" /></linearGradient>
    <linearGradient id={meterId + '-sheen'} x1="0" y1="0" x2="1" y2="0.6"><stop offset="0%" stop-color="#fff" stop-opacity="0" /><stop offset="30%" stop-color="#fff" stop-opacity={look.smoked ? 0.16 : 0.11} /><stop offset="44%" stop-color="#fff" stop-opacity="0.02" /><stop offset="58%" stop-color="#fff" stop-opacity="0" /></linearGradient>
    <radialGradient id={meterId + '-dome'} cx="40%" cy="30%" r="70%"><stop offset="0%" stop-color="#f6dc9a" /><stop offset="60%" stop-color="#9a7430" /><stop offset="100%" stop-color="#3a2a10" /></radialGradient>
    <filter id={meterId + '-grain'} x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency={size === 'full' ? 6 : 1.6} numOctaves="1" seed={channelIndex ? 5 : 9} /><feColorMatrix values="0 0 0 0 0.2  0 0 0 0 0.14  0 0 0 0 0.06  0 0 0 1.1 -0.45" /></filter>
    <filter id={meterId + '-soft'} x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="0.7" /></filter>
    <filter id={meterId + '-bloom'} x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="2.5" /></filter>
    <filter id={meterId + '-inset'} x="-10%" y="-10%" width="120%" height="120%"><feGaussianBlur stdDeviation="2" /></filter>
    <clipPath id={meterId + '-clip'}><path d={facePath} /></clipPath>
  </defs>
  <path d={bezelPath} fill="url(#{meterId}-bezel)" class="bezel" />
  <path d={facePath} fill="url(#{meterId}-lamp)" class="face" />
  <g clip-path="url(#{meterId}-clip)">
    {#if look.topLamp}<rect x="8" y="5.5" width="104" height="3" fill="#fff6dc" filter="url(#{meterId}-bloom)" class="lamp-strip" /><rect x="10" y="5.6" width="100" height="0.9" fill="#fffaf0" class="lamp-strip" />{/if}
    {#if look.frameGlow}<path d="M7 66 H113 M7 66 V40 M113 66 V40" stroke={look.frameGlow} stroke-width="1.6" fill="none" class="frame-glow" /><path d="M7 66 H113 M7 66 V40 M113 66 V40" stroke={look.frameGlow} stroke-width="4" fill="none" filter="url(#{meterId}-bloom)" class="frame-glow bloom" />{/if}
    <g class="ink">
      {#if look.top}<text x="60" y="17" class="title">{look.top}</text>{:else if look.scale === 'vu'}<text x="16" y="22" class="title vu-title">VU</text>{/if}
      <path d={arc(-48, angleFor(0), 52)} class="scale" />
      <path d={arc(angleFor(0), 46, 52)} class="scale hot-line" /><path d={arc(angleFor(0), 46, 54.5)} class="hot-band" />
      {#if look.segments}{#each Array.from({ length: 44 }, (unused, index) => index - 40) as decibels}{@const angle = powerAngle(decibels)}{@const [segmentX1, segmentY1] = point(angle, 53)}{@const [segmentX2, segmentY2] = point(angle, decibels % 10 === 0 ? 59 : 56.5)}<line x1={segmentX1} y1={segmentY1} x2={segmentX2} y2={segmentY2} class="tick segment" class:hot={decibels > 0} />{/each}{/if}
      {#each marks as [value, decibels]}
        {@const angle = angleFor(decibels)}{@const [innerX, innerY] = point(angle, 52)}{@const [outerX, outerY] = point(angle, 57)}{@const [labelX, labelY] = point(angle, 62.5)}
        {#if !look.segments}<line x1={innerX} y1={innerY} x2={outerX} y2={outerY} class="tick" class:hot={decibels > 0} />{/if}
        {#if look.markers}{@const [tipX, tipY] = point(angle, 53)}{@const [leftX, leftY] = point(angle - 1.6, 57.5)}{@const [rightX, rightY] = point(angle + 1.6, 57.5)}<path d="M{tipX} {tipY} L{leftX} {leftY} L{rightX} {rightY} Z" class="marker" class:hot={decibels > 0} />{/if}
        <text x={labelX} y={labelY} class="number" class:hot={decibels > 0}>{look.scale === 'vu' ? value : label(value)}</text>
      {/each}
      {#if look.percent}
        <path d={arc(vuAngle(-13.98), vuAngle(0), 48.5)} class="scale thin" />
        {#each percentMarks as [percent, decibels]}{@const angle = vuAngle(decibels)}{@const [tickX1, tickY1] = point(angle, 48.5)}{@const [tickX2, tickY2] = point(angle, 46.6)}{@const [textX, textY] = point(angle, 43.6)}<line x1={tickX1} y1={tickY1} x2={tickX2} y2={tickY2} class="tick thin" /><text x={textX} y={textY} class="small-number">{percent}</text>{/each}
      {/if}
      {#if look.decibels}
        <path d={arc(-48, 46, 48.5)} class="scale thin" />
        {#each decibelMarks as decibels}{@const angle = powerAngle(decibels)}{@const [tickX1, tickY1] = point(angle, 48.5)}{@const [tickX2, tickY2] = point(angle, 46.6)}{@const [textX, textY] = point(angle, 43.4)}<line x1={tickX1} y1={tickY1} x2={tickX2} y2={tickY2} class="tick thin" /><text x={textX} y={textY} class="small-number">{decibels > 0 ? '+' + decibels : decibels}</text>{/each}
      {/if}
      {#if look.bottom}<text x="60" y={look.pivot === 'mechanism' ? 52 : 55} class="subtitle">{look.bottom}</text>{/if}
      <text x="105" y="58" class="sign hot">+</text><text x="14" y="58" class="sign">−</text>
      <text x={look.pivot ? 20 : 60} y={look.pivot ? 63 : 61.5} class="channel">{channel}</text>
    </g>
    <rect x="0" y="0" width="120" height="72" fill="url(#{meterId}-lip)" class="shade" />
    <path d={facePath} class="shade inset" transform="translate(0 1.4)" filter="url(#{meterId}-inset)" />
    {#if size !== 'small'}<rect x="0" y="0" width="120" height="72" filter="url(#{meterId}-grain)" class="shade grain" />
      <line x1={pivotX + 1.6} y1={pivotY} x2={pivotX + 1.6} y2={pivotY - 60} class="needle-shadow" filter="url(#{meterId}-soft)" transform="rotate({needle} {pivotX} {pivotY - 1.2})" />{/if}
    <line x1={pivotX} y1={pivotY} x2={pivotX} y2={pivotY - 60} class="needle" stroke={look.needle} transform="rotate({needle} {pivotX} {pivotY})" />
    {#if look.pivot === 'mechanism'}<rect x="43" y="55" width="34" height="13" rx="2" class="mechanism" /><circle cx="48" cy="61" r="1.8" class="screw" /><circle cx="72" cy="61" r="1.8" class="screw" /><circle cx="60" cy="59" r="3.2" class="hub" />{/if}
    {#if look.pivot === 'dome'}<ellipse cx="60" cy="67.5" rx="10" ry="4.5" fill="url(#{meterId}-dome)" />{/if}
    {#if size !== 'small'}<path d={facePath} fill="url(#{meterId}-sheen)" class="shade" />{/if}
  </g>
  <path d={facePath} class="rim" />
</svg>

<style>
  .bezel { stroke: rgba(0, 0, 0, 0.5); stroke-width: 0.8; }
  .face { stroke: rgba(0, 0, 0, 0.45); stroke-width: 0.6; }
  .idle .face { filter: brightness(0.82) saturate(0.85); }
  .ink { fill: var(--ink); stroke: var(--ink); }
  .ink :is(text) { stroke: none; }
  .glow .ink, .blue .ink, .green .ink, .amber .ink { filter: drop-shadow(0 0 1.1px var(--glow)); }
  .idle .ink { opacity: 0.75; }
  .scale { fill: none; stroke-width: 0.9; stroke-linecap: round; } .scale.thin { stroke-width: 0.5; }
  .hot-line { stroke: var(--hot); } .hot-band { fill: none; stroke: var(--hot); stroke-width: 3; }
  .amber .hot-band, .green .hot-band { stroke-width: 1.6; }
  .tick { stroke-width: 0.9; stroke-linecap: round; } .tick.thin { stroke-width: 0.5; } .tick.segment { stroke-width: 1.1; stroke-linecap: butt; } .tick.hot { stroke: var(--hot); }
  .marker { stroke: none; fill: var(--ink); } .marker.hot { fill: var(--hot); }
  .number { font-size: 6px; text-anchor: middle; dominant-baseline: middle; font-family: "Segoe UI", Arial, sans-serif; font-weight: 600; } .number.hot, .sign.hot { fill: var(--hot); }
  .small-number { font-size: 3.6px; text-anchor: middle; dominant-baseline: middle; font-family: "Segoe UI", Arial, sans-serif; }
  .title { font-size: 5px; font-weight: 700; text-anchor: middle; letter-spacing: 0.5px; font-family: "Segoe UI", Arial, sans-serif; } .vu-title { font-size: 9px; text-anchor: start; letter-spacing: 0; }
  .subtitle { font-size: 4.2px; font-weight: 600; text-anchor: middle; letter-spacing: 1px; font-family: "Segoe UI", Arial, sans-serif; }
  .sign { font-size: 8px; font-weight: 700; text-anchor: middle; font-family: Arial, sans-serif; }
  .channel { font-size: 5px; text-anchor: middle; opacity: 0.55; font-family: var(--font-display); }
  .needle { stroke-width: 0.9; } .blue .needle, .green .needle, .amber .needle { filter: drop-shadow(0 0 1px var(--glow)); }
  .needle-shadow { stroke: #000; stroke-width: 1.4; opacity: 0.2; }
  .shade { pointer-events: none; } .inset { fill: none; stroke: #000; stroke-width: 4; opacity: 0.45; } .grain { opacity: 0.4; }
  .blue .grain, .green .grain, .amber .grain { opacity: 0.25; }
  .rim { fill: none; stroke: rgba(255, 255, 255, 0.22); stroke-width: 0.6; }
  .smoked .rim { stroke: rgba(255, 255, 255, 0.3); }
  .lamp-strip { opacity: 0.9; } .idle .lamp-strip { opacity: 0.5; }
  .frame-glow { opacity: 0.95; } .frame-glow.bloom { opacity: 0.55; } .idle .frame-glow { opacity: 0.55; }
  .mechanism { fill: #1d140a; stroke: #4a3720; stroke-width: 0.5; } .screw { fill: #6b5a44; stroke: #2a2014; stroke-width: 0.4; } .hub { fill: #2b2014; stroke: #8a7350; stroke-width: 0.5; }
</style>
