// Accent colour from the current cover (SPEC.md › Look and feel): the most vivid, reasonably bright colour in a 24×24 sample, applied sparingly via --accent
export async function accentFromCover(url: string): Promise<string | null> {
  const image = new Image(); image.crossOrigin = 'anonymous'; image.src = url
  try { await image.decode() } catch { return null }
  const canvas = document.createElement('canvas'); canvas.width = canvas.height = 24
  const context = canvas.getContext('2d', { willReadFrequently: true }); if (!context) return null
  context.drawImage(image, 0, 0, 24, 24)
  let pixels: Uint8ClampedArray
  try { pixels = context.getImageData(0, 0, 24, 24).data } catch { return null }
  let bestScore = 0, best: [number, number, number] | null = null
  for (let offset = 0; offset < pixels.length; offset += 4) {
    const red = pixels[offset] / 255, green = pixels[offset + 1] / 255, blue = pixels[offset + 2] / 255
    const brightest = Math.max(red, green, blue), darkest = Math.min(red, green, blue), saturation = brightest ? (brightest - darkest) / brightest : 0
    const score = saturation * (brightest > 0.35 ? 1 : 0.2) * (1 - Math.abs(brightest - 0.8))
    if (score > bestScore) { bestScore = score; best = [red, green, blue] }
  }
  if (!best || bestScore < 0.18) return null // a grey or black cover keeps the skin's own accent
  // Lift to a readable brightness, keeping the hue
  const lift = 0.85 / Math.max(best[0], best[1], best[2])
  return `rgb(${best.map(channel => Math.round(Math.min(1, channel * lift) * 255)).join(', ')})`
}
