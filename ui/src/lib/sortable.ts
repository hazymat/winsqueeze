// Press-and-hold reordering, after Sift's sortable.js: hold a row and it lifts with a springy wobble, follows the
// pointer, the other rows slide aside and a dotted slot shows where it will land. Esc puts it back.
//   use:sortable={{ onMove(from, to), onHold(index), group(index), holdMs, enabled }}   rows are the list's [data-sort-row] children
// group(index) returns the rows that travel with the held one (a multi-selection): they fly into it, it carries a count badge,
// and onMove's "to" is where the held row lands once the whole block is placed (see blockOrder)
export type SortableOptions = { onMove: (from: number, to: number) => void; onHold?: (index: number) => void; group?: (index: number) => number[]; holdMs?: number; enabled?: boolean }
const TILT = -0.8
const reducedMotion = () => matchMedia('(prefers-reduced-motion: reduce)').matches // Windows animation effects off: lift without the wobble
// A swing past the resting angle and back, smaller each time, like something physical picked up
const wobble = (fromAngle: number, toAngle: number, kick: number) => Array.from({ length: 41 }, (unused, step) => {
  const time = step / 40 * 0.7, fade = Math.exp(-time / 0.16), turn = 2 * Math.PI * 3.2 * time
  return { rotate: `${(toAngle + (fromAngle - toAngle) * fade * Math.cos(turn) + kick * fade * Math.sin(turn)).toFixed(3)}deg` }
})
const scrollerOf = (element: HTMLElement): HTMLElement | null => { let parent: HTMLElement | null = element; while (parent) { if (/(auto|scroll)/.test(getComputedStyle(parent).overflowY)) return parent; parent = parent.parentElement } return null }

export function sortable(listElement: HTMLElement, initialOptions: SortableOptions) {
  let options = initialOptions
  listElement.dataset.rowList = '' // selected neighbours merge into one block (tokens.css)
  let pressTimer = 0, pressRow: HTMLElement | null = null, startX = 0, startY = 0, lastY = 0, pointerId = 0
  // index/rows/rects are in "visible" terms (gathered rows taken out); fullIndex and moving are in the real list
  let drag: null | { index: number; fullIndex: number; moving: number[]; gathered: HTMLElement[]; badge: HTMLElement | null; ready: boolean; row: HTMLElement; allRows: HTMLElement[]; rows: HTMLElement[]; rects: DOMRect[]; target: number; slot: HTMLElement; scroller: HTMLElement | null; startScroll: number; scrollTimer: number } = null
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
    const allRows = rows(), fullIndex = allRows.indexOf(pressRow)
    const row = pressRow; cancelPress()
    options.onHold?.(fullIndex)
    const moving = (options.group?.(fullIndex) ?? [fullIndex]).filter(position => position >= 0 && position < allRows.length).sort((first, second) => first - second)
    if (!moving.includes(fullIndex)) moving.splice(0, moving.length, fullIndex)
    const gathered = moving.filter(position => position !== fullIndex).map(position => allRows[position])
    const slot = document.createElement('div'); slot.className = 'drop-slot'
    if (getComputedStyle(listElement).position === 'static') listElement.style.position = 'relative'
    const scroller = scrollerOf(listElement)
    drag = { index: fullIndex, fullIndex, moving, gathered, badge: null, ready: false, row, allRows, rows: allRows, rects: [], target: fullIndex, slot, scroller, startScroll: scroller?.scrollTop ?? 0, scrollTimer: 0 }
    row.classList.add('lifted'); document.body.classList.add('is-dragging')
    if (!reducedMotion()) row.animate(wobble(0, TILT, 2.4), { duration: 700, fill: 'forwards' })
    try { listElement.setPointerCapture(pointerId) } catch { }
    swallowClick = true
    if (!gathered.length) { startTracking(); return }
    // A group: the other chosen rows fly into the held one, then fold away so the block drags as one stacked row
    row.classList.add('stack')
    const badge = document.createElement('span'); badge.className = 'drag-count'; badge.textContent = String(moving.length); row.append(badge); drag.badge = badge
    const heldRect = row.getBoundingClientRect()
    for (const other of gathered) { const otherRect = other.getBoundingClientRect(); other.animate([{ transform: 'none', opacity: 1 }, { transform: `translateY(${heldRect.top - otherRect.top}px) scale(0.96)`, opacity: 0 }], { duration: reducedMotion() ? 1 : 180, easing: 'cubic-bezier(.4,0,.2,1)', fill: 'forwards' }) }
    window.setTimeout(() => {
      if (!drag) return
      const topBefore = row.getBoundingClientRect().top
      for (const other of gathered) other.classList.add('gathered')
      startY -= topBefore - row.getBoundingClientRect().top // rows folded away above it: keep it under the pointer
      startTracking()
    }, reducedMotion() ? 0 : 180)
  }

  // Measure the visible rows (gathered ones are folded away) and start following the pointer
  function startTracking() {
    if (!drag) return
    drag.rows = drag.allRows.filter(each => !drag!.gathered.includes(each))
    drag.index = drag.rows.indexOf(drag.row); drag.target = drag.index
    drag.rects = drag.rows.map(each => each.getBoundingClientRect())
    const listRect = listElement.getBoundingClientRect()
    Object.assign(drag.slot.style, { top: `${drag.rects[drag.index].top - listRect.top}px`, height: `${drag.rects[drag.index].height}px` })
    listElement.append(drag.slot)
    drag.ready = true
    move(lastY)
  }

  function move(pointerY: number) {
    if (!drag || !drag.ready) return
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
    const { index, target, row, allRows, slot, gathered, badge, moving, fullIndex, ready } = drag
    clearInterval(drag.scrollTimer)
    for (const each of allRows) { each.style.transition = ''; each.style.transform = '' }
    row.getAnimations().forEach(animation => animation.cancel())
    for (const other of gathered) { other.getAnimations().forEach(animation => animation.cancel()); other.classList.remove('gathered') }
    badge?.remove(); row.classList.remove('stack')
    if (!reducedMotion()) row.animate(wobble(TILT, 0, -1.2), { duration: 600 })
    row.classList.remove('lifted'); document.body.classList.remove('is-dragging'); slot.remove()
    drag = null
    // target is the block's slot among the rows left behind; report where the held row ends up in the full list
    if (commit && ready && (target !== index || gathered.length)) { const landed = target + moving.indexOf(fullIndex); if (gathered.length || landed !== fullIndex) options.onMove(fullIndex, landed) }
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
