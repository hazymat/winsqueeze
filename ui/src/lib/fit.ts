// A crowded row that has to hold longer translations: steps it through tighter layouts (classes fit-1, fit-2…) until nothing in it is cut off,
// checked again when it resizes or its text changes. Each component styles its own steps; data-fit-skip marks text that may be cut off (a long message)
export function fit(node: HTMLElement, steps = 2) {
  let frame = 0
  const cutOff = () => node.scrollWidth > node.clientWidth + 1 || [...node.querySelectorAll<HTMLElement>('div, button, span')].some(element => !element.closest('[data-fit-skip]') && element.scrollWidth > element.clientWidth + 1)
  const check = () => {
    for (let step = 1; step <= steps; step++) node.classList.remove('fit-' + step)
    for (let step = 1; step <= steps && cutOff(); step++) node.classList.add('fit-' + step)
  }
  const schedule = () => { cancelAnimationFrame(frame); frame = requestAnimationFrame(check) }
  // Watches the row and everything in it: text that grows once a font arrives resizes the items, not the row
  const resize = new ResizeObserver(schedule), watch = () => { resize.observe(node); node.querySelectorAll('*').forEach(element => resize.observe(element)) }
  const changes = new MutationObserver(() => { watch(); schedule() })
  watch(); changes.observe(node, { subtree: true, childList: true, characterData: true }); document.fonts?.ready.then(schedule); schedule()
  return { destroy() { resize.disconnect(); changes.disconnect(); cancelAnimationFrame(frame) } }
}

// Engraved legends (hi-fi faceplate, meters): too long a translation is drawn narrower, like a condensed typeface, to fit the space it has (its parent, less any `room` given)
export function squeeze(node: HTMLElement, room = 0) {
  let frame = 0
  const check = () => { node.style.transform = ''; const space = (node.parentElement?.clientWidth ?? 0) - room, need = Math.max(node.offsetWidth, node.scrollWidth); node.style.transform = space > 0 && need > space ? `scaleX(${(space / need).toFixed(3)})` : '' }
  const schedule = () => { cancelAnimationFrame(frame); frame = requestAnimationFrame(check) }
  const resize = new ResizeObserver(schedule), changes = new MutationObserver(schedule)
  resize.observe(node); if (node.parentElement) resize.observe(node.parentElement); changes.observe(node, { subtree: true, characterData: true, childList: true }); document.fonts?.ready.then(schedule); schedule()
  return { destroy() { resize.disconnect(); changes.disconnect(); cancelAnimationFrame(frame) } }
}
