<script lang="ts">
  // Pixel-exact level bars drawn on a canvas at the screen's real pixel density, so every segment and gap is the same width.
  //   vertical: two upright L/R bars made of 1-pixel scan lines, like the Squeezebox 2/3 display (transport bar)
  //   spectrum: Squeezebox-style frequency bands (one column per band, scan-line shading, floating peak markers)
  //   horizontal: segmented L/R bars with the top segments red (Now playing's bar graph)
  // Levels are 0..1; Squeezelite on Windows has no level output yet, so the bars sit unlit (dim scan lines)
  let { orientation = 'vertical', width = 26, height = 34, levels = [0, 0] }: { orientation?: 'vertical' | 'horizontal' | 'spectrum'; width?: number; height?: number; levels?: number[] } = $props()
  let canvas = $state<HTMLCanvasElement>()
  // Floating peak markers: each jumps to a new maximum, holds ~0.8 s, then falls slowly
  let peaks = $state<number[]>([])
  const heldUntil: number[] = []
  let frame = 0
  $effect(() => {
    const now = performance.now()
    peaks = levels.map((level, channel) => { const peak = peaks[channel] ?? 0; if (level >= peak) { heldUntil[channel] = now + 800; return level } return peak })
    if (!frame && peaks.some(peak => peak > 0)) frame = requestAnimationFrame(fall)
  })
  function fall(time: number) {
    peaks = peaks.map((peak, channel) => time < heldUntil[channel] ? peak : Math.max(levels[channel] ?? 0, peak - 0.012))
    frame = peaks.some((peak, channel) => peak > (levels[channel] ?? 0)) ? requestAnimationFrame(fall) : 0
  }
  $effect(() => () => cancelAnimationFrame(frame))
  const lit = '#5ee7ff', unlit = 'rgba(94, 231, 255, 0.13)', hot = '#ff5a46', hotUnlit = 'rgba(255, 90, 70, 0.16)'
  $effect(() => {
    if (!canvas) return
    const ratio = window.devicePixelRatio || 1, pixelWidth = Math.round(width * ratio), pixelHeight = Math.round(height * ratio)
    canvas.width = pixelWidth; canvas.height = pixelHeight
    const context = canvas.getContext('2d')!
    context.clearRect(0, 0, pixelWidth, pixelHeight)
    const unit = Math.max(1, Math.round(ratio)) // one display "pixel" in device pixels
    const rowPitch = 2 * unit, rows = Math.floor(pixelHeight / rowPitch)
    // A column of scan lines: lit up to its level, a floating line at its recent peak, dim above
    const column = (left: number, columnWidth: number, level: number, peak: number) => {
      const litRows = Math.round(level * rows), peakRow = peak > 0 ? Math.max(litRows, Math.round(peak * rows) - 1) : -1
      for (let row = 0; row < rows; row++) { context.fillStyle = row < litRows || row === peakRow ? lit : unlit; context.fillRect(left, pixelHeight - (row + 1) * rowPitch, columnWidth, unit) }
    }
    if (orientation === 'spectrum') {
      // Chunky blocks like the hardware: few bands, each a stack of segments, dim at the bottom and brightest at the top
      const bands = Math.max(1, levels.length), bandPitch = Math.floor(pixelWidth / bands), bandWidth = Math.max(unit, bandPitch - unit)
      const segmentPitch = 3 * unit, segmentHeight = 2 * unit, segments = Math.floor(pixelHeight / segmentPitch)
      const brightness = (segment: number) => 0.3 + 0.7 * Math.pow(segment / Math.max(1, segments - 1), 1.8)
      for (let band = 0; band < bands; band++) {
        const litSegments = Math.round((levels[band] ?? 0) * segments), peakSegment = (peaks[band] ?? 0) > 0 ? Math.max(litSegments, Math.round(peaks[band] * segments) - 1) : -1
        for (let segment = 0; segment < segments; segment++) {
          context.fillStyle = segment === peakSegment ? lit : segment < litSegments ? `rgba(94, 231, 255, ${brightness(segment).toFixed(2)})` : 'rgba(94, 231, 255, 0.09)'
          context.fillRect(band * bandPitch, pixelHeight - segment * segmentPitch - segmentHeight, bandWidth, segmentHeight)
        }
      }
    } else if (orientation === 'vertical') {
      // Two bars, a gap between; each bar is rows of one lit line and one dark line
      const gap = 3 * unit, barWidth = Math.floor((pixelWidth - gap) / 2)
      for (let channel = 0; channel < 2; channel++) column(channel * (barWidth + gap), barWidth, levels[channel] ?? 0, peaks[channel] ?? 0)
    } else {
      const segments = 16, hotFrom = 13, labelWidth = 0, rowGap = 3 * unit, barHeight = Math.floor((pixelHeight - rowGap) / 2)
      const pitch = Math.floor((pixelWidth - labelWidth) / segments), segmentWidth = pitch - 2 * unit
      for (let channel = 0; channel < 2; channel++) {
        const top = channel * (barHeight + rowGap), litSegments = Math.round((levels[channel] ?? 0) * segments), peakSegment = (peaks[channel] ?? 0) > 0 ? Math.max(litSegments, Math.round(peaks[channel] * segments) - 1) : -1
        for (let segment = 0; segment < segments; segment++) { context.fillStyle = segment < litSegments || segment === peakSegment ? (segment >= hotFrom ? hot : lit) : (segment >= hotFrom ? hotUnlit : unlit); context.fillRect(labelWidth + segment * pitch, top, segmentWidth, barHeight) }
      }
    }
  })
</script>

<canvas bind:this={canvas} style:width={`${width}px`} style:height={`${height}px`} aria-hidden="true"></canvas>

<style>
  canvas { display: block; image-rendering: pixelated; }
</style>
