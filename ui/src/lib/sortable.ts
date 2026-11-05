// Press-and-hold reordering, after Sift's sortable.js: hold a row and it lifts with a springy wobble, follows the
// pointer, the other rows slide aside and a dotted slot shows where it will land. Esc puts it back.
//   use:sortable={{ onMove(from, to), onHold(index), holdMs, enabled }}   rows are the list's [data-sort-row] children
export type SortableOptions = { onMove: (from: number, to: number) => void; onHold?: (index: number) => void; holdMs?: number; enabled?: boolean }
const TILT = -0.8
// A swing past the resting angle and back, smaller each time, like something physical picked up
const wobble = (fromAngle: number, toAngle: number, kick: number) => Array.from({ length: 41 }, (unused, step) => {
  const time = step / 40 * 0.7, fade = Math.exp(-time / 0.16), turn = 2 * Math.PI * 3.2 * time
  return { rotate: `${(toAngle + (fromAngle - toAngle) * fade * Math.cos(turn) + kick * fade * Math.sin(turn)).toFixed(3)}deg` }
})
const scrollerOf = (element: HTMLElement): HTMLElement | null => { let parent: HTMLElement | null = element; while (parent) { if (/(auto|scroll)/.test(getComputedStyle(parent).overflowY)) return parent; parent = parent.parentElement } return null }

export function sortable(listElement: HTMLElement, initialOptions: SortableOptions) {
  let options = initialOptions
  let pressTimer = 0, pressRow: HTMLElement | null = null, startX = 0, startY = 0, lastY = 0, pointerId = 0
  let drag: null | { index: number; row: HTMLElement; rows: HTMLElement[]; rects: DOMRect[]; target: number; slot: HTMLElement; scroller: HTMLElement | null; startScroll: number; scrollTimer: number } = null
  let swallowClick = false
  const rows = () => Array.from(listElement.children).filter(child => (child as HTMLElement).dataset.sortRow !== undefined) as HTMLElement[]

  function onPointerDown(event: PointerEvent) {
    if (options.enabled === false || event.button !== 0 || drag) return
    const row = (event.target as HTMLElement).closest('[data-sort-row]') as HTMLElement | null
    if (!row || row.parentElement !== listElement || (event.target as HTMLElement).closest('input, select, textarea, .no-hold')) return
    pressRow = row; startX = event.clientX; startY = lastY = event.clientY; pointerId = event.pointerId
    row.classList.add('pressing')
    pressTimer = window.setTimeout(lift, options.holdMs ?? 350)
  }
  function cancelPress() { clearTimeout(pressTimer); pressRow?.classList.remove('pressing'); pressRow = null }

  function lift() {
    if (!pressRow) return
    const allRows = rows(), index = allRows.indexOf(pressRow)
    const row = pressRow; cancelPress()
    const listRect = listElement.getBoundingClientRect(), rects = allRows.map(each => each.getBoundingClientRect())
    const slot = document.createElement('div'); slot.className = 'drop-slot'
    if (getComputedStyle(listElement).position === 'static') listElement.style.position = 'relative'
    Object.assign(slot.style, { top: `${rects[index].top - listRect.top}px`, height: `${rects[index].height}px` })
    listElement.append(slot)
    const scroller = scrollerOf(listElement)
    drag = { index, row, rows: allRows, rects, target: index, slot, scroller, startScroll: scroller?.scrollTop ?? 0, scrollTimer: 0 }
    row.classList.add('lifted'); document.body.classList.add('is-dragging')
    row.animate(wobble(0, TILT, 2.4), { duration: 700, fill: 'forwards' })
    try { listElement.setPointerCapture(pointerId) } catch { }
    swallowClick = true
    options.onHold?.(index)
    move(lastY)
  }

  function move(pointerY: number) {
    if (!drag) return
    const scrollDelta = (drag.scroller?.scrollTop ?? 0) - drag.startScroll
    const shift = pointerY - startY + scrollDelta
    drag.row.style.transform = `translateY(${shift}px)`
    const height = drag.rects[drag.index].height + (parseFloat(getComputedStyle(drag.row).marginBottom) || 0)
    const centre = drag.rects[drag.index].top + drag.rects[drag.index].height / 2 + shift
    let target = drag.index
    drag.rects.forEach((rect, rowIndex) => { const rowCentre = rect.top + rect.height / 2; if (rowIndex > drag!.index && centre > rowCentre) target = Math.max(target, rowIndex); if (rowIndex < drag!.index && centre < rowCentre) target = Math.min(target, rowIndex) })
    drag.target = target
    drag.rows.forEach((row, rowIndex) => { if (rowIndex === drag!.index) return; const offset = rowIndex > drag!.index && rowIndex <= target ? -height : rowIndex < drag!.index && rowIndex >= target ? height : 0; row.style.transition = 'transform 150ms cubic-bezier(.2,.8,.2,1)'; row.style.transform = offset ? `translateY(${offset}px)` : '' })
    const listTop = drag.rects[0].top - (drag.rows[0].offsetTop || 0)
    const slotTop = target > drag.index ? drag.rects[target].bottom - drag.rects[drag.index].height : drag.rects[target].top
    drag.slot.style.top = `${slotTop - listTop}px`
    // Near the scroller's edge: scroll, and keep tracking while the pointer rests there
    clearInterval(drag.scrollTimer)
    if (drag.scroller) {
      const scrollerRect = drag.scroller.getBoundingClientRect(), edge = 40
      const speed = pointerY < scrollerRect.top + edge ? -10 : pointerY > scrollerRect.bottom - edge ? 10 : 0
      if (speed) drag.scrollTimer = window.setInterval(() => { drag?.scroller?.scrollBy(0, speed); move(lastY) }, 30)
    }
  }

  function finish(commit: boolean) {
    if (!drag) return
    const { index, target, row, rows: allRows, slot } = drag
    clearInterval(drag.scrollTimer)
    for (const each of allRows) { each.style.transition = ''; each.style.transform = '' }
    row.getAnimations().forEach(animation => animation.cancel())
    row.animate(wobble(TILT, 0, -1.2), { duration: 600 })
    row.classList.remove('lifted'); document.body.classList.remove('is-dragging'); slot.remove()
    drag = null
    if (commit && target !== index) options.onMove(index, target)
  }

  function onPointerMove(event: PointerEvent) {
    lastY = event.clientY
    if (pressRow && Math.hypot(event.clientX - startX, event.clientY - startY) > 6) cancelPress() // a scroll or a plain drag, not a hold
    if (drag) move(event.clientY)
  }
  function onPointerUp() { cancelPress(); if (drag) finish(true) }
  function onKeyDown(event: KeyboardEvent) { if (event.key === 'Escape' && drag) { event.stopPropagation(); finish(false) } }
  // The click that ends a hold or a drag isn't a click on the row
  function onClick(event: MouseEvent) { if (swallowClick) { swallowClick = false; event.stopPropagation(); event.preventDefault() } }
  function onPointerDownReset() { swallowClick = false }

  // Touch: once a row is carried the page must not scroll under the finger
  function onTouchMove(event: TouchEvent) { if (drag && event.cancelable) event.preventDefault() }
  document.addEventListener('touchmove', onTouchMove, { passive: false })
  listElement.addEventListener('pointerdown', onPointerDownReset, true)
  listElement.addEventListener('pointerdown', onPointerDown)
  listElement.addEventListener('click', onClick, true)
  window.addEventListener('pointermove', onPointerMove)
  window.addEventListener('pointerup', onPointerUp)
  window.addEventListener('pointercancel', onPointerUp)
  window.addEventListener('keydown', onKeyDown, true)
  return {
    update(newOptions: SortableOptions) { options = newOptions },
    destroy() {
      listElement.removeEventListener('pointerdown', onPointerDownReset, true); listElement.removeEventListener('pointerdown', onPointerDown); listElement.removeEventListener('click', onClick, true)
      document.removeEventListener('touchmove', onTouchMove); window.removeEventListener('pointermove', onPointerMove); window.removeEventListener('pointerup', onPointerUp); window.removeEventListener('pointercancel', onPointerUp); window.removeEventListener('keydown', onKeyDown, true)
    }
  }
}

// Moves that turn one order into another with single-item moves (Lyrion's "playlist move from to"), fewest for a block
export function planMoves(currentOrder: number[], finalOrder: number[]): [number, number][] {
  const working = currentOrder.slice(), moves: [number, number][] = []
  finalOrder.forEach((wanted, position) => { const from = working.indexOf(wanted); if (from !== position) { working.splice(position, 0, working.splice(from, 1)[0]); moves.push([from, position]) } })
  return moves
}
// The order after moving the selected positions (as a block, kept in order) to where the dragged one was dropped
export function blockOrder(count: number, selected: number[], from: number, to: number): number[] {
  const order = Array.from({ length: count }, (unused, position) => position)
  const moving = selected.includes(from) ? selected.slice().sort((first, second) => first - second) : [from]
  const rest = order.filter(position => !moving.includes(position))
  // "to" is the dragged row's index in the final list; place the block so the dragged row lands there
  const insertAt = Math.max(0, Math.min(rest.length, to - moving.indexOf(from)))
  return rest.slice(0, insertAt).concat(moving, rest.slice(insertAt))
}
