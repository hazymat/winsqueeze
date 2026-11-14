<script lang="ts" module>
  // Bundled faces for VU Builder meters (all SIL Open Font Licence); a face only loads when a meter uses it
  import '@fontsource/jost/400.css'; import '@fontsource/cinzel/400.css'; import '@fontsource/poiret-one/400.css'; import '@fontsource/limelight/400.css'
  import '@fontsource/michroma/400.css'; import '@fontsource/courier-prime/400.css'; import '@fontsource/barlow-condensed/500.css'; import '@fontsource/vt323/400.css'
  export type VuDesign = { font: string, fontSize: number, label: string, brand: string, roundness: number, arc: string, weight: number, cold: string, hot: string, needleColour: string, needleShape: string, needleFrom: string, base: string, back: string, lamp: string, lampFrom: string, display: string, dotSize: number, glow: boolean, glass: string, casing: string }
  // Same values as CustomMeter's defaults in the shell (the Dark room VU)
  export const darkRoom: VuDesign = { font: 'modern', fontSize: 1, label: 'vu', brand: '', roundness: 0.15, arc: 'band', weight: 1.4, cold: '#3fa57a', hot: '#b5402f', needleColour: '#ecd9a8', needleShape: 'blade', needleFrom: 'bottom', base: 'ring', back: '#000000', lamp: 'none', lampFrom: 'below', display: 'analogue', dotSize: 3, glow: false, glass: 'none', casing: 'line' }
  const fontFamilies: Record<string, string> = { modern: 'Jost', engraved: 'Cinzel', deco: '"Poiret One"', thirties: 'Limelight', hifi: 'Michroma', typewriter: '"Courier Prime"', condensed: '"Barlow Condensed"', terminal: 'VT323' }
  const lampColours: Record<string, string> = { tungsten: '#ffc36e', warm: '#ffe2b0', cool: '#dff1ff', amber: '#ff9a2e', green: '#7dffb0', blue: '#6cc8ff' }
  // VU marks along the scale, as a share of its length
  const scaleMarks: [number, number][] = [[-20, 0], [-10, 0.2], [-7, 0.32], [-5, 0.43], [-3, 0.56], [-2, 0.63], [-1, 0.71], [0, 0.79], [1, 0.86], [2, 0.93], [3, 1]]
  let meterCount = 0
</script>
<script lang="ts">
  // A meter drawn from a VU Builder design (SPEC.md › VU Builder): face 240 x 140, scale on a circular arc whose flatness is the design's roundness, needle from a pivot just outside the face
  let { design, channel, decibels, reference, reading = null }: { design: VuDesign, channel: string, decibels: number, reference: number, reading?: number | null } = $props()
  const id = `custom-vu-${meterCount++}`
  const spec = $derived(Object.assign({}, darkRoom, design ?? {}) as VuDesign)
  const fromTop = $derived(spec.needleFrom === 'top'), out = $derived(fromTop ? -1 : 1)
  const radius = $derived(100 + (1 - Math.max(0, Math.min(1, spec.roundness))) * 420), halfSpan = $derived(Math.asin(98 / radius))
  const scaleTop = $derived(fromTop ? 44 : 52), pivotY = $derived(fromTop ? -6 : 150)
  const onScale = (share: number, beyond: number) => { const angle = -halfSpan + 2 * halfSpan * share, centreY = scaleTop + radius, along = radius + beyond, y = centreY - along * Math.cos(angle); return [120 + along * Math.sin(angle), fromTop ? 140 - y : y] }
  const arcPath = (from: number, to: number, beyond: number) => { const [startX, startY] = onScale(from, beyond), [endX, endY] = onScale(to, beyond); return `M${startX} ${startY} A${radius + beyond} ${radius + beyond} 0 0 ${fromTop ? 0 : 1} ${endX} ${endY}` }
  const zeroShare = 0.79
  // dB -> share of the scale; below -20 the needle sinks a little past the end to its rest
  const shareOf = (vu: number) => {
    if (vu <= -20) return -0.07 + 0.07 * Math.max(0, Math.min(1, (vu + 30) / 10))
    for (let mark = 1; mark < scaleMarks.length; mark++) if (vu <= scaleMarks[mark][0]) { const [lowDb, lowShare] = scaleMarks[mark - 1], [highDb, highShare] = scaleMarks[mark]; return lowShare + (highShare - lowShare) * (vu - lowDb) / (highDb - lowDb) }
    return Math.min(1.06, 1 + (vu - 3) * 0.02)
  }
  const needleShare = $derived(reading ?? shareOf(decibels - reference))
  const needleAngle = $derived.by(() => { const [tipX, tipY] = onScale(needleShare, 4 * out); return Math.atan2(tipX - 120, pivotY - tipY) * 180 / Math.PI })
  const needleLength = $derived.by(() => { const [tipX, tipY] = onScale(Math.max(0, Math.min(1, needleShare)), 4 * out); return Math.hypot(tipX - 120, tipY - pivotY) })
  const needleWidth = $derived(spec.needleShape === 'bar' ? 2.2 : spec.needleShape === 'line' ? 0.8 : 2.4), tipWidth = $derived(spec.needleShape === 'blade' ? 0.35 : needleWidth)
  const needlePath = $derived(fromTop ? `M${120 - needleWidth} ${pivotY} L${120 - tipWidth} ${pivotY + needleLength} L${120 + tipWidth} ${pivotY + needleLength} L${120 + needleWidth} ${pivotY} Z` : `M${120 - needleWidth} ${pivotY} L${120 - tipWidth} ${pivotY - needleLength} L${120 + tipWidth} ${pivotY - needleLength} L${120 + needleWidth} ${pivotY} Z`)
  const font = $derived(fontFamilies[spec.font] ?? 'Jost'), textSize = $derived(9 * spec.fontSize)
  const label = $derived(spec.label === 'channel' ? channel : spec.label === 'reference' ? `0 VU = ${Math.round(reference)} dBFS`.replace('-', '−') : spec.label === 'none' ? '' : 'VU')
  // A bulb shows strongly on a pale face and only as a glow on a dark one
  const backLightness = $derived.by(() => { const hex = /^#?([0-9a-f]{6})$/i.exec(spec.back)?.[1] ?? '000000'; return (parseInt(hex.slice(0, 2), 16) * 0.3 + parseInt(hex.slice(2, 4), 16) * 0.59 + parseInt(hex.slice(4, 6), 16) * 0.11) / 255 })
  const lampAt = $derived(({ below: ['50%', '115%'], above: ['50%', '-15%'], left: ['-10%', '60%'], right: ['110%', '60%'] } as Record<string, string[]>)[spec.lampFrom] ?? ['50%', '115%'])
  const baseY = $derived(fromTop ? 0 : 140), sweep = $derived(fromTop ? 0 : 1)
  const corner = $derived(spec.casing === 'line' ? 10 : 4)
</script>

<svg viewBox="0 0 240 140" class="custom-vu" aria-hidden="true">
  <defs>
    {#if spec.lamp !== 'none'}<radialGradient id="{id}-lamp" cx={lampAt[0]} cy={lampAt[1]} r="85%"><stop offset="0" stop-color={lampColours[spec.lamp] ?? '#ffc36e'} stop-opacity={backLightness < 0.35 ? 0.32 : 0.75} /><stop offset="1" stop-color={lampColours[spec.lamp] ?? '#ffc36e'} stop-opacity="0" /></radialGradient>{/if}
    <radialGradient id="{id}-dome" cx="45%" cy={fromTop ? '80%' : '20%'} r="80%"><stop offset="0" stop-color="#4a4640" /><stop offset="1" stop-color="#0c0b0a" /></radialGradient>
    <linearGradient id="{id}-sheen" x1="0" y1="0" x2="1" y2="0.8"><stop offset="0" stop-color="#fff" stop-opacity="0" /><stop offset="0.3" stop-color="#fff" stop-opacity="0.13" /><stop offset="0.45" stop-color="#fff" stop-opacity="0.02" /><stop offset="1" stop-color="#fff" stop-opacity="0" /></linearGradient>
    <linearGradient id="{id}-wood" x1="0" y1="0" x2="1" y2="0.2"><stop offset="0" stop-color="#5a3317" /><stop offset="0.3" stop-color="#7d4a22" /><stop offset="0.6" stop-color="#4c2a12" /><stop offset="1" stop-color="#6e3f1c" /></linearGradient>
    <linearGradient id="{id}-metal" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#e3e4e6" /><stop offset="0.5" stop-color="#a9acb0" /><stop offset="1" stop-color="#d2d4d7" /></linearGradient>
    {#if spec.display === 'dots'}<pattern id="{id}-grid" width={spec.dotSize} height={spec.dotSize} patternUnits="userSpaceOnUse"><rect x={spec.dotSize * 0.12} y={spec.dotSize * 0.12} width={spec.dotSize * 0.76} height={spec.dotSize * 0.76} rx={spec.dotSize * 0.2} fill="#fff" /></pattern>
      <mask id="{id}-dots" maskUnits="userSpaceOnUse" x="0" y="0" width="240" height="140"><rect width="240" height="140" fill="url(#{id}-grid)" /></mask>{/if}
    {#if spec.glow}<filter id="{id}-glow" x="-10%" y="-10%" width="120%" height="120%"><feGaussianBlur stdDeviation="1.3" result="blur" /><feMerge><feMergeNode in="blur" /><feMergeNode in="SourceGraphic" /></feMerge></filter>{/if}
    <clipPath id="{id}-clip"><rect width="240" height="140" rx={corner} /></clipPath>
  </defs>
  <g clip-path="url(#{id}-clip)">
    <rect width="240" height="140" fill={spec.back} />
    {#if spec.lamp !== 'none'}<rect width="240" height="140" fill="url(#{id}-lamp)" />{/if}
    <g mask={spec.display === 'dots' ? `url(#${id}-dots)` : null} filter={spec.glow ? `url(#${id}-glow)` : null} font-family={font}>
      {#if spec.arc !== 'ticks'}<path d={arcPath(0, zeroShare, 0)} stroke={spec.cold} stroke-width={spec.weight} fill="none" /><path d={arcPath(zeroShare, 1, 0)} stroke={spec.hot} stroke-width={spec.weight} fill="none" />{/if}
      {#if spec.arc === 'band'}<path d={arcPath(zeroShare, 1, 3.2 * out)} stroke={spec.hot} stroke-width={spec.weight * 3.4} fill="none" />{/if}
      {#each scaleMarks as [markDb, share]}
        {@const colour = markDb > 0 ? spec.hot : spec.cold}{@const long = markDb === 0 || markDb === -20 || spec.arc === 'ticks'}
        {@const [innerX, innerY] = onScale(share, spec.arc === 'ticks' ? -2 * out : 0)}{@const [outerX, outerY] = onScale(share, (long ? 9 : 6) * out)}{@const [labelX, labelY] = onScale(share, 17 * out)}
        <line x1={innerX} y1={innerY} x2={outerX} y2={outerY} stroke={colour} stroke-width={spec.weight * 1.1} stroke-linecap={spec.font === 'terminal' ? 'square' : 'round'} />
        {#if markDb !== -2 && markDb !== -1 && markDb !== 1}<text x={labelX} y={labelY} fill={colour} font-size={textSize} text-anchor="middle" dominant-baseline="middle">{markDb > 0 ? '+' + markDb : Math.abs(markDb)}</text>{/if}
      {/each}
      {#if label}<text x="120" y={fromTop ? 124 : 104} fill={spec.cold} font-size={spec.label === 'reference' ? textSize * 0.7 : textSize * 1.35} text-anchor="middle" letter-spacing="1.5" opacity="0.9">{label}</text>{/if}
      {#if spec.brand}<text x="22" y="20" fill={spec.cold} font-size={textSize * 0.55} letter-spacing="2" opacity="0.6">{spec.brand}</text>{/if}
      <path d={needlePath} fill={spec.needleColour} transform="rotate({needleAngle} 120 {pivotY})" class="needle" />
    </g>
    {#if spec.base === 'ring'}<path d="M100 {baseY} A20 14 0 0 {sweep} 140 {baseY}" fill={spec.back} stroke={spec.cold} stroke-width="1" />
    {:else if spec.base === 'filled'}<path d="M100 {baseY} A20 14 0 0 {sweep} 140 {baseY}" fill={spec.cold} />
    {:else if spec.base === 'dome'}<path d="M92 {baseY} A28 19 0 0 {sweep} 148 {baseY}" fill="url(#{id}-dome)" />{/if}
    {#if spec.glass !== 'none'}<rect width="240" height="140" fill="url(#{id}-sheen)" />{#if spec.glass === 'smoked'}<rect width="240" height="140" fill="#000" opacity="0.28" />{/if}{/if}
  </g>
  {#if spec.casing === 'line'}<rect x="3" y="3" width="234" height="134" rx="10" fill="none" stroke={spec.cold} stroke-width="1.4" />
  {:else if spec.casing === 'bezel'}<rect x="1.5" y="1.5" width="237" height="137" rx="6" fill="none" stroke="#1b1c1f" stroke-width="7" /><rect x="5" y="5" width="230" height="130" rx="3" fill="none" stroke="#000" stroke-width="1" opacity="0.6" />
  {:else if spec.casing === 'wood'}<rect x="2.5" y="2.5" width="235" height="135" rx="4" fill="none" stroke="url(#{id}-wood)" stroke-width="9" /><rect x="7" y="7" width="226" height="126" fill="none" stroke="#000" stroke-width="1.2" opacity="0.5" />
  {:else if spec.casing === 'metal'}<rect x="2.5" y="2.5" width="235" height="135" rx="3" fill="none" stroke="url(#{id}-metal)" stroke-width="9" />{/if}
</svg>

<style>
  .custom-vu { display: block; overflow: visible; }
</style>
