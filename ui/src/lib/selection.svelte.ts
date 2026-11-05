// Choosing several rows (after Sift's list kit): press and hold a row, or Ctrl/Shift+click, and the action bar
// (built into the layout above the transport, ActionStrip.svelte) offers what can be done with them.
// One list at a time owns the selection (its scope). Delete runs the action keyed 'Delete'; Esc clears.
export type MenuChoice = { label: string; detail?: string; run: () => void | Promise<void> }
export type SelectionAction = { label: string; key?: string; danger?: boolean; single?: boolean; run?: (keys: string[]) => void | Promise<void>; menu?: (keys: string[]) => Promise<MenuChoice[]> }
export const selection = $state({ scope: '', keys: [] as string[], anchor: '', actions: [] as SelectionAction[], noun: 'item' })

export const isSelected = (scope: string, key: string) => selection.scope === scope && selection.keys.includes(key)
export function clearSelection() { selection.scope = ''; selection.keys = []; selection.anchor = ''; selection.actions = []; const focused = document.activeElement as HTMLElement | null; if (focused?.closest?.('[data-sort-row], [data-select-key]')) focused.blur() }
export function selectKeys(scope: string, keys: string[], actions: SelectionAction[], noun = 'item') { selection.scope = scope; selection.keys = keys; selection.anchor = keys[keys.length - 1] ?? ''; selection.actions = actions; selection.noun = noun }

// A press and hold picks the row (adding to a selection already in this list)
export function selectOnHold(scope: string, key: string, actions: SelectionAction[], noun = 'item') {
  if (selection.scope !== scope) selectKeys(scope, [key], actions, noun)
  else if (!selection.keys.includes(key)) { selection.keys = selection.keys.concat([key]); selection.anchor = key }
}
// A click on a row: true when it was a selection gesture (so the row's own action, e.g. play, doesn't run)
export function rowClick(event: MouseEvent, scope: string, key: string, allKeys: string[], actions: SelectionAction[], noun = 'item') {
  const inScope = selection.scope === scope && selection.keys.length > 0
  if (event.shiftKey) {
    const anchorIndex = Math.max(0, allKeys.indexOf(inScope ? selection.anchor : key)), clickedIndex = allKeys.indexOf(key)
    const range = allKeys.slice(Math.min(anchorIndex, clickedIndex), Math.max(anchorIndex, clickedIndex) + 1)
    selectKeys(scope, inScope ? Array.from(new Set(selection.keys.concat(range))) : range, actions, noun); selection.anchor = allKeys[anchorIndex]
    return true
  }
  if (event.ctrlKey || event.metaKey || inScope) {
    if (selection.scope !== scope) selectKeys(scope, [], actions, noun)
    selection.keys = selection.keys.includes(key) ? selection.keys.filter(selectedKey => selectedKey !== key) : selection.keys.concat([key])
    selection.anchor = key; selection.actions = actions
    if (!selection.keys.length) clearSelection()
    return true
  }
  return false
}
export async function runAction(action: SelectionAction) { const keys = selection.keys.slice(); clearSelection(); await action.run?.(keys) }

addEventListener('keydown', (event: KeyboardEvent) => {
  if (!selection.keys.length || (event.target as HTMLElement)?.matches?.('input, textarea, select')) return
  if (event.key === 'Escape') { event.preventDefault(); clearSelection(); return }
  const action = selection.actions.find(candidate => candidate.key === event.key || (event.key === 'Backspace' && candidate.key === 'Delete'))
  if (action?.run && !(action.single && selection.keys.length > 1)) { event.preventDefault(); runAction(action) }
})
