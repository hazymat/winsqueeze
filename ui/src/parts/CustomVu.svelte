<script lang="ts" module>
  // Bundled faces for VU Builder meters (all SIL Open Font Licence); a face only loads when a meter uses it
  import '@fontsource/jost/400.css'; import '@fontsource/cinzel/400.css'; import '@fontsource/poiret-one/400.css'; import '@fontsource/limelight/400.css'
  import '@fontsource/michroma/400.css'; import '@fontsource/courier-prime/400.css'; import '@fontsource/barlow-condensed/500.css'; import '@fontsource/vt323/400.css'
  export type VuDesign = { font: string, fontSize: number, label: string, brand: string, roundness: number, arc: string, weight: number, cold: string, hot: string, needleColour: string, needleShape: string, needleFinish: string, shadow: number, needleFrom: string, base: string, back: string, lamp: string, lampTint: string, lampFrom: string, lampPower: number, display: string, glow: boolean, glass: string, casing: string }
  // Same values as CustomMeter's defaults in the shell (the Dark room VU)
  export const darkRoom: VuDesign = { font: 'modern', fontSize: 1, label: 'vu', brand: '', roundness: 0.15, arc: 'band', weight: 1.4, cold: '#3fa57a', hot: '#b5402f', needleColour: '#ecd9a8', needleShape: 'blade', needleFinish: 'flat', shadow: 0.5, needleFrom: 'bottom', base: 'ring', back: '#000000', lamp: 'none', lampTint: 'natural', lampFrom: 'below', lampPower: 0.5, display: 'analogue', glow: false, glass: 'none', casing: 'line' }
  const fontFamilies: Record<string, string> = { modern: 'Jost', engraved: 'Cinzel', deco: '"Poiret One"', thirties: 'Limelight', hifi: 'Michroma', typewriter: '"Courier Prime"', condensed: '"Barlow Condensed"', terminal: 'VT323' }
  const wideFonts: Record<string, number> = { hifi: 1.45, thirties: 1.2, engraved: 1.15, typewriter: 1.15, terminal: 0.9, condensed: 0.75 } // how wide "+3" sets, against Jost
  // Lamps: how many sources and how far along the edge they sit, how far the light reaches, how hot the centre of each is, their natural colour, how soft a shadow they cast and how they come on
  type Lamp = { count: number, spread: number, reach: number, core: number, colour: string, soft: number, start: string }
  const lamps: Record<string, Lamp> = {
    tungsten: { count: 2, spread: 0.35, reach: 1, core: 0.8, colour: '#ffb45a', soft: 1.1, start: 'warm' }, // two small bulbs: warm pools, glowing up as the filaments heat
    halogen: { count: 2, spread: 0.3, reach: 1.15, core: 1, colour: '#fff1d6', soft: 0.6, start: 'quick' }, // brighter, whiter, tighter hot spots and crisp shadows
    pilot: { count: 1, spread: 0, reach: 0.85, core: 1, colour: '#ff9d3c', soft: 0.8, start: 'warm' }, // one small bulb in the middle: a hot spot falling away fast to the corners
    led: { count: 7, spread: 0.92, reach: 0.75, core: 0.15, colour: '#fff4e4', soft: 2, start: 'instant' }, // a row of small sources: an even wash along the edge
    tube: { count: 13, spread: 0.96, reach: 0.8, core: 0, colour: '#e8f3ff', soft: 2.6, start: 'strike' }, // a fluorescent tube: one long even band, flickering as it strikes
    neon: { count: 2, spread: 0.5, reach: 0.45, core: 0.3, colour: '#ff5a2a', soft: 1.6, start: 'shimmer' } // neon glow lamps: short orange-red reach, never quite steady
  }
  const tints: Record<string, string> = { amber: '#ff8c1a', red: '#ff3b2a', green: '#6dffa8', blue: '#5cbcff' } // a coloured gel over the lamp
  const oldLamps: Record<string, string[]> = { warm: ['halogen', 'natural'], cool: ['tube', 'natural'], amber: ['tungsten', 'amber'], green: ['tungsten', 'green'], blue: ['tungsten', 'blue'] } // an earlier dev build's bulb colours
  // Casing: how deep the frame is, and its outer corner (the inner corner follows, so the face's corners match the frame)
  const casings: Record<string, { depth: number, corner: number }> = { none: { depth: 0, corner: 4 }, line: { depth: 3, corner: 10 }, bezel: { depth: 9, corner: 12 }, metal: { depth: 11, corner: 12 }, wood: { depth: 14, corner: 10 }, acrylic: { depth: 9, corner: 14 } }
  // VU marks along the scale, as a share of its length
  const scaleMarks: [number, number][] = [[-20, 0], [-10, 0.2], [-7, 0.32], [-5, 0.43], [-3, 0.56], [-2, 0.63], [-1, 0.71], [0, 0.79], [1, 0.86], [2, 0.93], [3, 1]]
  const zeroShare = 0.79
  let meterCount = 0
  // A rounded rectangle as a path, for frames cut out with evenodd
  const roundedRect = (left: number, top: number, width: number, height: number, corner: number) => `M${left + corner} ${top}H${left + width - corner}A${corner} ${corner} 0 0 1 ${left + width} ${top + corner}V${top + height - corner}A${corner} ${corner} 0 0 1 ${left + width - corner} ${top + height}H${left + corner}A${corner} ${corner} 0 0 1 ${left} ${top + height - corner}V${top + corner}A${corner} ${corner} 0 0 1 ${left + corner} ${top}Z`
</script>
<script lang="ts">
  // A meter drawn from a VU Builder design (SPEC.md › VU Builder): face 240 x 140 inside its casing, scale on a circular arc whose flatness is the design's roundness,
  // sized so its numbers always fit; the needle swings from the arc's centre (behind a cover at the face's edge when the arc is flat), so it never changes length; bulbs behind the casing light the face
  let { design, channel, decibels, reference, reading = null }: { design: VuDesign, channel: string, decibels: number, reference: number, reading?: number | null } = $props()
  const id = `custom-vu-${meterCount++}`
  const spec = $derived(Object.assign({}, darkRoom, design ?? {}) as VuDesign)
  const oled = $derived(spec.display === 'oled' || spec.display === 'dots') // dots: an earlier dev build's display, shown as OLED
  const casing = $derived(casings[spec.casing] ?? casings.none), depth = $derived(casing.depth), outerCorner = $derived(casing.corner), innerCorner = $derived(Math.max(1.5, casing.corner - casing.depth * 0.75))
  const inner = $derived({ left: depth, top: depth, right: 240 - depth, bottom: 140 - depth })
  const fromTop = $derived(spec.needleFrom === 'top'), out = $derived(fromTop ? -1 : 1)
  const font = $derived(fontFamilies[spec.font] ?? 'Jost'), textSize = $derived(8.5 * spec.fontSize)
  // Scale: the circle's radius sets the flatness; its span is as wide as the face allows once the end numbers (set 17 units out) are inside it
  const radius = $derived(95 + (1 - Math.max(0, Math.min(1, spec.roundness))) * 200)
  const halfSpan = $derived(Math.asin(Math.min(0.97, (120 - depth - 6 - textSize * (wideFonts[spec.font] ?? 1)) / (radius + 17))))
  const numberSize = $derived(Math.min(textSize, 0.12 * 2 * halfSpan * (radius + 17) / (1.55 * (wideFonts[spec.font] ?? 1)))) // the tightest gap (10 to 7) still fits its numbers
  const scaleEdge = $derived((fromTop ? 140 - inner.bottom : inner.top) + 34 + textSize * 1.3) // the arc's highest point (lowest when the needle hangs)
  const pivotY = $derived(fromTop ? 140 - scaleEdge - radius : scaleEdge + radius) // the arc's centre, at or past the face's edge
  const onScale = (share: number, beyond: number) => { const angle = -halfSpan + 2 * halfSpan * share, centreY = scaleEdge + radius, along = radius + beyond, y = centreY - along * Math.cos(angle); return [120 + along * Math.sin(angle), fromTop ? 140 - y : y] }
  const arcPath = (from: number, to: number, beyond: number) => { const [startX, startY] = onScale(from, beyond), [endX, endY] = onScale(to, beyond); return `M${startX} ${startY} A${radius + beyond} ${radius + beyond} 0 0 ${fromTop ? 0 : 1} ${endX} ${endY}` }
  // dB -> share of the scale; below -20 the needle sinks a little past the end to its rest
  const shareOf = (vu: number) => {
    if (vu <= -20) return -0.07 + 0.07 * Math.max(0, Math.min(1, (vu + 30) / 10))
    for (let mark = 1; mark < scaleMarks.length; mark++) if (vu <= scaleMarks[mark][0]) { const [lowDb, lowShare] = scaleMarks[mark - 1], [highDb, highShare] = scaleMarks[mark]; return lowShare + (highShare - lowShare) * (vu - lowDb) / (highDb - lowDb) }
    return Math.min(1.06, 1 + (vu - 3) * 0.02)
  }
  const needleShare = $derived(reading ?? shareOf(decibels - reference))
  // The needle is drawn pointing straight up from the pivot and turned onto its reading (a hanging one turns past 90°, so it points down)
  const needleAngle = $derived.by(() => { const [tipX, tipY] = onScale(needleShare, 4); return Math.atan2(tipX - 120, pivotY - tipY) * 180 / Math.PI })
  const needleLength = $derived(radius + 4)
  const needleShape = $derived.by(() => {
    const length = needleLength, shape = spec.needleShape
    const shaft = (base: number, tip: number) => `M${-base} 0 L${-tip} ${-length} L${tip} ${-length} L${base} 0 Z`
    if (shape === 'line') return shaft(0.45, 0.45)
    if (shape === 'bar') return shaft(2.2, 2.2)
    if (shape === 'blade') return shaft(2.4, 0.35)
    // Fine needles with a shape near the tip
    const fine = `M-0.9 0 L-0.3 ${-length + 2} L0.3 ${-length + 2} L0.9 0 Z`
    if (shape === 'arrow') return `M-0.9 0 L-0.3 ${-length + 8} L0.3 ${-length + 8} L0.9 0 Z M0 ${-length - 1} L-2.6 ${-length + 9} L0 ${-length + 7} L2.6 ${-length + 9} Z`
    if (shape === 'lance') return fine + ` M0 ${-length - 1} L-1.9 ${-length + 7} L0 ${-length + 17} L1.9 ${-length + 7} Z`
    if (shape === 'spade') return fine + ` M0 ${-length - 1} C-3.2 ${-length + 4} -3.2 ${-length + 9} 0 ${-length + 12} C3.2 ${-length + 9} 3.2 ${-length + 4} 0 ${-length - 1} Z`
    if (shape === 'flag') return fine + ` M-0.6 ${-length + 22} L-3.4 ${-length + 23} L-3.4 ${-length + 31} L-0.6 ${-length + 32} Z`
    if (shape === 'ball') return `M-0.8 0 L-0.3 ${-length + 3} L0.3 ${-length + 3} L0.8 0 Z M0 ${-length - 0.6} a2.2 2.2 0 1 0 0.01 0 Z`
    return shaft(2.4, 0.35)
  })
  const label = $derived(spec.label === 'channel' ? channel : spec.label === 'reference' ? `0 VU = ${Math.round(reference)} dBFS`.replace('-', '−') : spec.label === 'none' ? '' : 'VU')
  const labelY = $derived(fromTop ? inner.top + 30 + textSize * 0.9 : inner.bottom - 26)
  // Light: lamps hidden just behind the casing's edge. A pale face is dim where they don't reach; a dark one only glows near them
  const backLightness = $derived.by(() => { const hex = /^#?([0-9a-f]{6})$/i.exec(spec.back)?.[1] ?? '000000'; return (parseInt(hex.slice(0, 2), 16) * 0.3 + parseInt(hex.slice(2, 4), 16) * 0.59 + parseInt(hex.slice(4, 6), 16) * 0.11) / 255 })
  const lampType = $derived(oldLamps[spec.lamp]?.[0] ?? spec.lamp), lampTint = $derived(oldLamps[spec.lamp]?.[1] ?? spec.lampTint)
  const lamp = $derived(lamps[lampType] ?? lamps.tungsten), lit = $derived(lampType in lamps)
  const lampColour = $derived(tints[lampTint] ?? lamp.colour), coreColour = $derived(lamp.core > 0.5 ? (lampTint in tints ? '#fff4e0' : '#ffffff') : lampColour)
  const bulbs = $derived.by((): number[][] => {
    const along = Array.from({ length: lamp.count }, (unused, index) => lamp.count === 1 ? 0.5 : 0.5 + (index / (lamp.count - 1) - 0.5) * lamp.spread)
    const width = inner.right - inner.left, height = inner.bottom - inner.top
    if (spec.lampFrom === 'above') return along.map(share => [inner.left + share * width, inner.top - 3])
    if (spec.lampFrom === 'left') return along.map(share => [inner.left - 3, inner.top + share * height])
    if (spec.lampFrom === 'right') return along.map(share => [inner.right + 3, inner.top + share * height])
    return along.map(share => [inner.left + share * width, inner.bottom + 3])
  })
  const eachBulb = $derived(Math.min(1, 2.2 / Math.pow(bulbs.length, 0.6))) // many small sources add up to the same light as two bulbs
  const shadowBulbs = $derived(bulbs.length <= 3 ? bulbs : [bulbs[0], bulbs[Math.floor(bulbs.length / 2)], bulbs[bulbs.length - 1]]) // a long source's shadow: its ends and middle
  const pale = $derived(backLightness > 0.35)
  // Needle finish: polished metal catches the light; where depends on the needle's angle against the bulbs (or the room light above when they're off)
  const finishes: Record<string, { body: string, light: string, dark: string }> = { brass: { body: '#b8913f', light: '#fff1c2', dark: '#4d3510' }, chrome: { body: '#a9adb3', light: '#ffffff', dark: '#3a3d42' }, black: { body: '#141414', light: '#8a8a8a', dark: '#000000' } }
  const finish = $derived(finishes[spec.needleFinish] ?? null)
  const lightAngle = $derived.by(() => { const [lightX, lightY] = lit ? bulbs.reduce((sum, bulb) => [sum[0] + bulb[0] / bulbs.length, sum[1] + bulb[1] / bulbs.length], [0, 0]) : [120, -300]; return Math.atan2(lightX - 120, pivotY - lightY) * 180 / Math.PI })
  const turnFromLight = $derived(((needleAngle - lightAngle + 540) % 360) - 180) // -180..180: how far the needle is turned away from the light
  const highlightAcross = $derived(Math.max(0.08, Math.min(0.92, 0.5 - turnFromLight / 140))) // across the needle: the side facing the light
  const glintAlong = $derived(Math.max(0.05, Math.min(0.95, 0.55 + Math.sin(turnFromLight * Math.PI / 180) * 0.45))) // along the needle: the glint slides as it swings
  const power = $derived(Math.max(0, Math.min(1, spec.lampPower ?? 0.5))) // how bright the bulbs are, and so how far their light reaches across the face
  // Segments: each step between two marks split evenly, so every mark falls on a gap
  const segments = $derived.by(() => { const list: number[][] = []; for (let mark = 1; mark < scaleMarks.length; mark++) { const from = scaleMarks[mark - 1][1], to = scaleMarks[mark][1], count = Math.max(1, Math.round((to - from) * 28)); for (let step = 0; step < count; step++) list.push([from + (to - from) * step / count, from + (to - from) * (step + 1) / count]) } return list })
  // The cover over the needle's pivot: a disc round the arc's centre that shows just past the face's edge
  const baseY = $derived(fromTop ? inner.top : inner.bottom), pivotDepth = $derived(Math.abs(pivotY - baseY))
  const cover = (peek: number) => pivotDepth + peek
  const coverSpan = (peek: number) => Math.acos(Math.min(1, pivotDepth / cover(peek))) // half the angle of the cover that shows
  // The needle's shadow on the face: it stands a few mm off the face, so each bulb throws it a little away from itself
  const shadowOffset = (bulb: number[]) => { const [tipX, tipY] = onScale(needleShare, 0), middleX = (tipX + 120) / 2, middleY = (tipY + pivotY) / 2; return [(middleX - bulb[0]) * 0.035, (middleY - bulb[1]) * 0.035] }
  const ring = $derived(roundedRect(0, 0, 240, 140, outerCorner) + roundedRect(inner.left, inner.top, inner.right - inner.left, inner.bottom - inner.top, innerCorner))
  const innerPath = $derived(roundedRect(inner.left, inner.top, inner.right - inner.left, inner.bottom - inner.top, innerCorner))
  const screwY = $derived(fromTop ? inner.top + 6 : inner.bottom - 6), highlightY = $derived(baseY - out * 13)
</script>

<svg viewBox="0 0 240 140" class="custom-vu" aria-hidden="true">
  <defs>
    <clipPath id="{id}-face"><path d={innerPath} /></clipPath>
    {#each bulbs as [bulbX, bulbY], bulbIndex}
      <radialGradient id="{id}-pool-{bulbIndex}" gradientUnits="userSpaceOnUse" cx={bulbX} cy={bulbY} r={(70 + power * 150) * lamp.reach}><stop offset="0" stop-color={coreColour} /><stop offset={0.04 + lamp.core * 0.08} stop-color={coreColour} stop-opacity={0.4 + lamp.core * 0.6} /><stop offset="0.3" stop-color={lampColour} /><stop offset="0.65" stop-color={lampColour} stop-opacity="0.4" /><stop offset="1" stop-color={lampColour} stop-opacity="0" /></radialGradient>
      <radialGradient id="{id}-glow-{bulbIndex}" gradientUnits="userSpaceOnUse" cx={bulbX} cy={bulbY} r={(35 + power * 80) * lamp.reach}><stop offset="0" stop-color={lamp.core > 0.5 ? coreColour : lampColour} stop-opacity="0.9" /><stop offset="0.25" stop-color={lampColour} stop-opacity="0.35" /><stop offset="1" stop-color={lampColour} stop-opacity="0" /></radialGradient>
    {/each}
    <filter id="{id}-soft" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation={lit ? lamp.soft : 1.1} /></filter>
    <filter id="{id}-lip" x="-5%" y="-5%" width="110%" height="110%"><feGaussianBlur stdDeviation="2.4" /></filter>
    <!-- OLED: crisp ink lit pixel by pixel on a fine grid, with only a faint bloom -->
    <pattern id="{id}-pixels" width="1.2" height="1.2" patternUnits="userSpaceOnUse"><rect x="0.12" y="0.12" width="0.96" height="0.96" rx="0.18" fill="#fff" /></pattern>
    <mask id="{id}-pixel-mask" maskUnits="userSpaceOnUse" x="0" y="0" width="240" height="140"><rect width="240" height="140" fill="url(#{id}-pixels)" /></mask>
    <filter id="{id}-oled" x="-5%" y="-5%" width="110%" height="110%"><feGaussianBlur in="SourceGraphic" stdDeviation="1.4" result="halo" /><feComponentTransfer in="halo" result="halo"><feFuncA type="linear" slope="0.35" /></feComponentTransfer><feMerge><feMergeNode in="halo" /><feMergeNode in="SourceGraphic" /></feMerge></filter>
    <filter id="{id}-glow" x="-10%" y="-10%" width="120%" height="120%"><feGaussianBlur stdDeviation="1.3" result="blur" /><feMerge><feMergeNode in="blur" /><feMergeNode in="SourceGraphic" /></feMerge></filter>
    <!-- Wood: long grain from stretched noise, coloured through a walnut ramp; the side rails run the other way -->
    <filter id="{id}-grain-across" x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency="0.006 0.55" numOctaves="4" seed="5" /><feColorMatrix type="matrix" values="0.62 0 0 0 0.16  0.36 0 0 0 0.07  0.16 0 0 0 0.02  0 0 0 0 1" /><feComposite in2="SourceGraphic" operator="in" /></filter>
    <filter id="{id}-grain-down" x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency="0.55 0.006" numOctaves="4" seed="9" /><feColorMatrix type="matrix" values="0.62 0 0 0 0.16  0.36 0 0 0 0.07  0.16 0 0 0 0.02  0 0 0 0 1" /><feComposite in2="SourceGraphic" operator="in" /></filter>
    <filter id="{id}-brushed" x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency="0.004 0.9" numOctaves="2" seed="3" /><feColorMatrix values="0 0 0 0 0.55  0 0 0 0 0.56  0 0 0 0 0.58  0 0 0 0.55 0" /><feComposite in2="SourceGraphic" operator="in" /></filter>
    <linearGradient id="{id}-metal" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#e9eaec" /><stop offset="0.45" stop-color="#b5b8bc" /><stop offset="1" stop-color="#8e9196" /></linearGradient>
    <linearGradient id="{id}-bezel" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#3a3c40" /><stop offset="0.12" stop-color="#1c1d20" /><stop offset="1" stop-color="#0b0b0c" /></linearGradient>
    <linearGradient id="{id}-acrylic" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#ffffff" stop-opacity="0.32" /><stop offset="0.4" stop-color="#ffffff" stop-opacity="0.08" /><stop offset="1" stop-color="#ffffff" stop-opacity="0.18" /></linearGradient>
    <linearGradient id="{id}-sheen" x1="0" y1="0" x2="1" y2="0.8"><stop offset="0" stop-color="#fff" stop-opacity="0" /><stop offset="0.3" stop-color="#fff" stop-opacity="0.14" /><stop offset="0.45" stop-color="#fff" stop-opacity="0.02" /><stop offset="1" stop-color="#fff" stop-opacity="0" /></linearGradient>
    {#if finish}
      <linearGradient id="{id}-across" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color={finish.dark} /><stop offset={Math.max(0, highlightAcross - 0.22)} stop-color={finish.body} /><stop offset={highlightAcross} stop-color={finish.light} /><stop offset={Math.min(1, highlightAcross + 0.22)} stop-color={finish.body} /><stop offset="1" stop-color={finish.dark} /></linearGradient>
      <linearGradient id="{id}-along" x1="0" y1="1" x2="0" y2="0"><stop offset={Math.max(0, glintAlong - 0.12)} stop-color="#fff" stop-opacity="0" /><stop offset={glintAlong} stop-color="#fff" stop-opacity={lit ? 0.85 : 0.5} /><stop offset={Math.min(1, glintAlong + 0.12)} stop-color="#fff" stop-opacity="0" /></linearGradient>
    {/if}
    <radialGradient id="{id}-dome" gradientUnits="userSpaceOnUse" cx="110" cy={highlightY} r="34"><stop offset="0" stop-color="#55504a" /><stop offset="0.6" stop-color="#1d1b19" /><stop offset="1" stop-color="#070707" /></radialGradient>
    <radialGradient id="{id}-brass" gradientUnits="userSpaceOnUse" cx="112" cy={highlightY} r="32"><stop offset="0" stop-color="#fff0b8" /><stop offset="0.45" stop-color="#c39a45" /><stop offset="1" stop-color="#4a3410" /></radialGradient>
    <radialGradient id="{id}-chrome-band" gradientUnits="userSpaceOnUse" cx="108" cy={highlightY} r="40"><stop offset="0" stop-color="#ffffff" stop-opacity="0.95" /><stop offset="0.5" stop-color="#c9ccd1" stop-opacity="0.9" /><stop offset="1" stop-color="#6a6d72" stop-opacity="0.9" /></radialGradient>
    <radialGradient id="{id}-chrome" cx="38%" cy="32%" r="75%"><stop offset="0" stop-color="#ffffff" /><stop offset="0.5" stop-color="#b9bcc1" /><stop offset="1" stop-color="#45484d" /></radialGradient>
  </defs>

  <!-- Face, lit -->
  <g clip-path="url(#{id}-face)">
    <rect width="240" height="140" fill={oled ? '#020303' : spec.back} />
    {#if lit && pale && !oled}
      <!-- What the bulbs light: the face in their colour near them, dim further away (multiplied over face, print and needle alike) -->
      <g class="multiply"><rect width="240" height="140" fill="#857a6c" /><g class="lamp {lamp.start}">{#each bulbs as unusedBulb, bulbIndex}<rect width="240" height="140" fill="url(#{id}-pool-{bulbIndex})" class="screen" opacity={eachBulb} />{/each}</g></g>
    {/if}
    <g filter={oled ? `url(#${id}-oled)` : spec.glow ? `url(#${id}-glow)` : null}><g mask={oled ? `url(#${id}-pixel-mask)` : null} font-family={font}>
      {#if spec.arc === 'line' || spec.arc === 'band'}<path d={arcPath(0, zeroShare, 0)} stroke={spec.cold} stroke-width={spec.weight} fill="none" /><path d={arcPath(zeroShare, 1, 0)} stroke={spec.hot} stroke-width={spec.weight} fill="none" />{/if}
      {#if spec.arc === 'band'}<path d={arcPath(zeroShare, 1, 3.2)} stroke={spec.hot} stroke-width={spec.weight * 3.4} fill="none" />{/if}
      {#if spec.arc === 'double'}{#each [0, 2.6] as beyond}<path d={arcPath(0, zeroShare, beyond)} stroke={spec.cold} stroke-width={spec.weight * 0.6} fill="none" /><path d={arcPath(zeroShare, 1, beyond)} stroke={spec.hot} stroke-width={spec.weight * 0.6} fill="none" />{/each}{/if}
      {#if spec.arc === 'wide'}<path d={arcPath(0, zeroShare, 2.4)} stroke={spec.cold} stroke-width={spec.weight * 3.6} fill="none" opacity="0.85" /><path d={arcPath(zeroShare, 1, 2.4)} stroke={spec.hot} stroke-width={spec.weight * 3.6} fill="none" />{/if}
      {#if spec.arc === 'dotted'}{#each Array.from({ length: 41 }, (unused, index) => index / 40) as share}{@const [dotX, dotY] = onScale(share, 0)}<circle cx={dotX} cy={dotY} r={spec.weight * 0.75} fill={share > zeroShare + 0.001 ? spec.hot : spec.cold} />{/each}{/if}
      {#if spec.arc === 'segments'}{#each segments as [from, to]}<path d={arcPath(from + 0.005, to - 0.005, 2.4)} stroke={from >= zeroShare - 0.001 ? spec.hot : spec.cold} stroke-width={spec.weight * 3.4} fill="none" />{/each}{/if}
      {#each scaleMarks as [markDb, share]}
        {@const colour = markDb > 0 ? spec.hot : spec.cold}{@const long = markDb === 0 || markDb === -20 || spec.arc === 'ticks'}
        {@const [innerX, innerY] = onScale(share, spec.arc === 'ticks' ? -2 : spec.arc === 'segments' ? 5.5 : 0)}{@const [outerX, outerY] = onScale(share, spec.arc === 'segments' ? (long ? 11 : 9) : long ? 9 : 6)}{@const [labelX, labelY] = onScale(share, 17)}
        <line x1={innerX} y1={innerY} x2={outerX} y2={outerY} stroke={colour} stroke-width={spec.weight * 1.1} stroke-linecap={spec.font === 'terminal' ? 'square' : 'round'} />
        {#if markDb !== -2 && markDb !== -1 && markDb !== 1}<text x={labelX} y={labelY} fill={colour} font-size={numberSize} text-anchor="middle" dominant-baseline="middle">{markDb > 0 ? '+' + markDb : Math.abs(markDb)}</text>{/if}
      {/each}
      {#if label}<text x="120" y={labelY} fill={spec.cold} font-size={spec.label === 'reference' ? textSize * 0.7 : textSize * 1.35} text-anchor="middle" letter-spacing="1.5" opacity="0.9">{label}</text>{/if}
      {#if spec.brand}<text x={inner.left + 10} y={inner.bottom - 8} fill={spec.cold} font-size={textSize * 0.55} letter-spacing="2" opacity="0.6">{spec.brand}</text>{/if}
      {#if pale && !oled && spec.shadow > 0}{#each lit ? shadowBulbs : [[90, -200]] as bulb}{@const [shiftX, shiftY] = shadowOffset(bulb)}<path d={needleShape} fill="#1a1006" opacity={Math.min(0.75, spec.shadow * (lit ? (shadowBulbs.length > 2 ? 0.32 : 0.5) : 0.6))} filter="url(#{id}-soft)" transform="translate({120 + shiftX * (0.4 + spec.shadow * 1.6)} {pivotY + shiftY * (0.4 + spec.shadow * 1.6)}) rotate({needleAngle})" />{/each}{/if}
      <g transform="translate(120 {pivotY}) rotate({needleAngle})">
        <path d={needleShape} fill={finish ? finish.body : spec.needleColour} />
        {#if finish && !oled}<!-- Polished needle: a bright line along the side facing the light, and a glint where the bulb reflects, both moving as the needle swings -->
          <path d={needleShape} fill="url(#{id}-across)" /><path d={needleShape} fill="url(#{id}-along)" class="screen" />{/if}
      </g>
    </g></g>
    {#if lit && (!pale || oled)}<g class="lamp {lamp.start}">{#each bulbs as unusedBulb, bulbIndex}<rect width="240" height="140" fill="url(#{id}-glow-{bulbIndex})" class="screen" opacity={(oled ? 0.35 : 0.6) * eachBulb} />{/each}</g>{/if}
    <!-- Base the needle rises from -->
    {#if spec.base === 'ring'}<circle cx="120" cy={pivotY} r={cover(14)} fill={oled ? '#020303' : spec.back} stroke={spec.cold} stroke-width="1" />
    {:else if spec.base === 'filled'}<circle cx="120" cy={pivotY} r={cover(14)} fill={spec.cold} />
    {:else if spec.base === 'dome' || spec.base === 'screw'}<circle cx="120" cy={pivotY} r={cover(19)} fill="url(#{id}-dome)" /><circle cx="120" cy={pivotY} r={cover(17.5)} fill="none" stroke="#fff" stroke-opacity="0.14" stroke-width="0.6" />
      {#if spec.base === 'screw'}<circle cx="120" cy={screwY} r="4.2" fill="url(#{id}-chrome)" stroke="#111" stroke-width="0.4" /><line x1="116.6" y1={screwY} x2="123.4" y2={screwY} stroke="#2a2c30" stroke-width="0.9" stroke-linecap="round" transform="rotate(-24 120 {screwY})" />{/if}
    {:else if spec.base === 'knurled'}<circle cx="120" cy={pivotY} r={cover(17)} fill="url(#{id}-dome)" /><circle cx="120" cy={pivotY} r={cover(17)} fill="url(#{id}-chrome-band)" />
      {#each Array.from({ length: 31 }, (unused, index) => -coverSpan(17) + index * 2 * coverSpan(17) / 30) as angle}<line x1={120 + cover(12.6) * Math.sin(angle)} y1={pivotY - out * cover(12.6) * Math.cos(angle)} x2={120 + cover(17) * Math.sin(angle)} y2={pivotY - out * cover(17) * Math.cos(angle)} stroke="#3c3f44" stroke-width="0.7" />{/each}
      <circle cx="120" cy={pivotY} r={cover(12.6)} fill="url(#{id}-dome)" /><circle cx="120" cy={pivotY} r={cover(12.6)} fill="url(#{id}-chrome-band)" stroke="#55585d" stroke-width="0.4" /><circle cx="120" cy={screwY} r="2" fill="#2a2c30" />
    {:else if spec.base === 'brass'}<circle cx="120" cy={pivotY} r={cover(18)} fill="url(#{id}-brass)" stroke="#3a2808" stroke-width="0.5" /><circle cx="120" cy={screwY} r="3.6" fill="url(#{id}-brass)" stroke="#3a2808" stroke-width="0.4" /><line x1="117.2" y1={screwY} x2="122.8" y2={screwY} stroke="#3a2808" stroke-width="0.8" stroke-linecap="round" transform="rotate(18 120 {screwY})" />{/if}
    <!-- Depth: the casing's lip shades the face's edge (deepest along the top), and light through the edge where the bulbs are -->
    {#if depth >= 9}<path d={innerPath} fill="none" stroke="#000" stroke-width="7" opacity="0.55" filter="url(#{id}-lip)" transform="translate(0 1.6)" />{/if}
    {#if lit && depth >= 9 && spec.casing !== 'wood'}{#each bulbs as unusedBulb, bulbIndex}<path d={innerPath} fill="none" stroke="url(#{id}-glow-{bulbIndex})" stroke-width="3" opacity={0.9 * eachBulb} />{/each}{/if}
    {#if spec.glass !== 'none'}<rect width="240" height="140" fill="url(#{id}-sheen)" />{/if}
  </g>

  <!-- Casing -->
  {#if spec.casing === 'line'}<path d={roundedRect(1.5, 1.5, 237, 137, outerCorner - 1.5)} fill="none" stroke={spec.cold} stroke-width="1.4" />
  {:else if spec.casing === 'bezel'}
    <path d={ring} fill="url(#{id}-bezel)" fill-rule="evenodd" />
    <path d={roundedRect(0.5, 0.5, 239, 139, outerCorner - 0.5)} fill="none" stroke="#5a5d63" stroke-width="0.6" opacity="0.7" />
    <path d={innerPath} fill="none" stroke="#000" stroke-width="1.2" />
  {:else if spec.casing === 'metal'}
    <path d={ring} fill="url(#{id}-metal)" fill-rule="evenodd" /><path d={ring} fill="#fff" fill-rule="evenodd" filter="url(#{id}-brushed)" opacity="0.55" />
    <!-- Chamfered inner edge: the top wall in shadow, the bottom catching the light -->
    <path d={innerPath} fill="none" stroke="#5c5f64" stroke-width="2.4" transform="translate(0 -0.6)" opacity="0.8" /><path d={innerPath} fill="none" stroke="#fbfbfc" stroke-width="1" transform="translate(0 1)" opacity="0.7" />
    <path d={roundedRect(0.4, 0.4, 239.2, 139.2, outerCorner - 0.4)} fill="none" stroke="#fff" stroke-width="0.6" opacity="0.7" />
  {:else if spec.casing === 'wood'}
    <!-- Four mitred rails, grain running along each -->
    <clipPath id="{id}-ring"><path d={ring} clip-rule="evenodd" /></clipPath>
    <g clip-path="url(#{id}-ring)">
      <polygon points="0,0 240,0 {240 - depth},{depth} {depth},{depth}" fill="#fff" filter="url(#{id}-grain-across)" /><polygon points="0,140 240,140 {240 - depth},{140 - depth} {depth},{140 - depth}" fill="#fff" filter="url(#{id}-grain-across)" />
      <polygon points="0,0 {depth},{depth} {depth},{140 - depth} 0,140" fill="#fff" filter="url(#{id}-grain-down)" /><polygon points="240,0 {240 - depth},{depth} {240 - depth},{140 - depth} 240,140" fill="#fff" filter="url(#{id}-grain-down)" />
      <!-- Light from above: top rail bright, bottom in shade, mitre joints as fine dark lines -->
      <polygon points="0,0 240,0 {240 - depth},{depth} {depth},{depth}" fill="#fff" opacity="0.1" /><polygon points="0,140 240,140 {240 - depth},{140 - depth} {depth},{140 - depth}" fill="#000" opacity="0.28" />
      <polygon points="0,0 {depth},{depth} {depth},{140 - depth} 0,140" fill="#000" opacity="0.08" /><polygon points="240,0 {240 - depth},{depth} {240 - depth},{140 - depth} 240,140" fill="#000" opacity="0.16" />
      {#each [[0, 0, depth, depth], [240, 0, 240 - depth, depth], [0, 140, depth, 140 - depth], [240, 140, 240 - depth, 140 - depth]] as [fromX, fromY, toX, toY]}<line x1={fromX} y1={fromY} x2={toX} y2={toY} stroke="#1c0e05" stroke-width="0.5" opacity="0.7" />{/each}
    </g>
    <path d={innerPath} fill="none" stroke="#120802" stroke-width="1.4" /><path d={roundedRect(0.4, 0.4, 239.2, 139.2, outerCorner - 0.4)} fill="none" stroke="#c99a68" stroke-width="0.6" opacity="0.5" />
  {:else if spec.casing === 'acrylic'}
    <!-- A thick clear frame: dark through its depth, bright where light bends through the edges -->
    <path d={ring} fill="#0d0f12" fill-opacity="0.82" fill-rule="evenodd" /><path d={ring} fill="url(#{id}-acrylic)" fill-rule="evenodd" />
    <path d={roundedRect(0.7, 0.7, 238.6, 138.6, outerCorner - 0.7)} fill="none" stroke="#fff" stroke-width="1.1" opacity="0.45" />
    <path d={innerPath} fill="none" stroke="#fff" stroke-width="1.3" opacity="0.5" /><path d={innerPath} fill="none" stroke="#000" stroke-width="0.6" opacity="0.8" transform="translate(0 -0.8)" />
    {#if lit}{#each bulbs as unusedBulb, bulbIndex}<path d={ring} fill="url(#{id}-glow-{bulbIndex})" fill-rule="evenodd" class="screen" opacity={eachBulb} />{/each}{/if}
  {/if}
</svg>

<style>
  .custom-vu { display: block; overflow: visible; }
  .multiply { mix-blend-mode: multiply; isolation: isolate; } .screen { mix-blend-mode: screen; }
  /* How each lamp comes on: filaments glow up, a tube strikes, neon never quite settles */
  .lamp.warm { animation: lamp-warm 1.1s ease-in; } .lamp.quick { animation: lamp-warm 0.25s ease-out; }
  .lamp.strike { animation: lamp-strike 0.9s steps(1, end); } .lamp.shimmer { animation: lamp-shimmer 2.3s ease-in-out infinite; }
  @keyframes lamp-warm { from { opacity: 0; } }
  @keyframes lamp-strike { 0% { opacity: 0; } 12% { opacity: 0.9; } 18% { opacity: 0.1; } 30% { opacity: 1; } 36% { opacity: 0.25; } 52% { opacity: 1; } 58% { opacity: 0.6; } 64% { opacity: 1; } }
  @keyframes lamp-shimmer { 0%, 100% { opacity: 1; } 31% { opacity: 0.93; } 47% { opacity: 0.99; } 72% { opacity: 0.9; } }
</style>
