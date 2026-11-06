// Press-and-hold reordering for tiles and cards in a grid (sortable.ts handles single-column lists): the held item lifts and
// follows the pointer, the item it will take the place of is outlined, and letting go moves it there. Esc puts it back.
//   use:gridSortable={{ onMove(from, to), holdMs }}   items are the grid's [data-sort-row] children
export type GridSortableOptions = { onMove: (from: number, to: number) => void; holdMs?: number }

export function gridSortable(gridElement: HTMLElement, initialOptions: GridSortableOptions) {
  let options = initialOptions, pressTimer = 0, startX = 0, startY = 0, swallowClick = false
  let held: null | { item: HTMLElement; from: number; to: number; items: HTMLElement[] } = null
  const items = () => Array.from(gridElement.children).filter(child => (child as HTMLElement).dataset.sortRow !== undefined) as HTMLElement[]
  const clearMarks = () => { for (const item of items()) item.classList.remove('drop-target') }
  function finish(commit: boolean) {
    if (!held) return
    const { item, from, to } = held
    held = null; clearMarks(); item.classList.remove('lifted'); item.style.transform = ''
    if (commit && from !== to) options.onMove(from, to)
  }
  function onPointerDown(event: PointerEvent) {
    if (event.button !== 0 || held) return
    const item = (event.target as HTMLElement).closest('[data-sort-row]') as HTMLElement | null
    if (!item || item.parentElement !== gridElement) return
    startX = event.clientX; startY = event.clientY
    clearTimeout(pressTimer)
    pressTimer = window.setTimeout(() => { const all = items(), from = all.indexOf(item); held = { item, from, to: from, items: all }; item.classList.add('lifted'); swallowClick = true; navigator.vibrate?.(15) }, options.holdMs ?? 450)
  }
  function onPointerMove(event: PointerEvent) {
    if (!held) { if (Math.hypot(event.clientX - startX, event.clientY - startY) > 6) clearTimeout(pressTimer); return }
    event.preventDefault()
    held.item.style.transform = `translate(${event.clientX - startX}px, ${event.clientY - startY}px) scale(1.04)`
    // The item whose box is nearest the pointer is where it lands
    let nearest = held.from, nearestDistance = Infinity
    held.items.forEach((item, index) => { if (item === held!.item) return; const rect = item.getBoundingClientRect(), distance = Math.hypot(event.clientX - (rect.left + rect.width / 2), event.clientY - (rect.top + rect.height / 2)); if (distance < nearestDistance) { nearestDistance = distance; nearest = index } })
    const heldRect = held.item.getBoundingClientRect(), heldDistance = Math.hypot(event.clientX - (heldRect.left - (event.clientX - startX) + heldRect.width / 2), event.clientY - (heldRect.top - (event.clientY - startY) + heldRect.height / 2))
    held.to = heldDistance < nearestDistance ? held.from : nearest
    clearMarks(); if (held.to !== held.from) held.items[held.to].classList.add('drop-target')
  }
  const onPointerUp = () => { clearTimeout(pressTimer); finish(true) }
  const onKeyDown = (event: KeyboardEvent) => { if (event.key === 'Escape' && held) { event.preventDefault(); finish(false) } }
  const onClick = (event: MouseEvent) => { if (swallowClick) { swallowClick = false; event.stopPropagation(); event.preventDefault() } }
  const onContextMenu = (event: MouseEvent) => { if (held || swallowClick) event.preventDefault() }
  gridElement.addEventListener('pointerdown', onPointerDown)
  gridElement.addEventListener('click', onClick, true)
  gridElement.addEventListener('contextmenu', onContextMenu)
  window.addEventListener('pointermove', onPointerMove)
  window.addEventListener('pointerup', onPointerUp)
  window.addEventListener('pointercancel', onPointerUp)
  window.addEventListener('keydown', onKeyDown)
  return {
    update(newOptions: GridSortableOptions) { options = newOptions },
    destroy() { clearTimeout(pressTimer); gridElement.removeEventListener('pointerdown', onPointerDown); gridElement.removeEventListener('click', onClick, true); gridElement.removeEventListener('contextmenu', onContextMenu); window.removeEventListener('pointermove', onPointerMove); window.removeEventListener('pointerup', onPointerUp); window.removeEventListener('pointercancel', onPointerUp); window.removeEventListener('keydown', onKeyDown) }
  }
}
