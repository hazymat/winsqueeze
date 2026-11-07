// Damaged files (SPEC.md › Damaged files): the shell spots decode errors while a track plays; this marks those tracks and says what happened
import { onShellMessage, sendToShell } from '../bridge'
import { toast, openSection } from '../state.svelte'

export const damaged = $state({ ids: {} as Record<string, true> })
export const isDamaged = (trackId: unknown) => !!damaged.ids[String(trackId)]
onShellMessage('damagedFiles', message => { const ids: Record<string, true> = {}; for (const trackId of message.ids ?? []) ids[String(trackId)] = true; damaged.ids = ids })
onShellMessage('damagedFound', message => {
  const text = message.what === 'skipped' ? `Skipped “${message.title}”: the file is damaged` : message.what === 'unplayable' ? `Can’t play “${message.title}”: the file is damaged` : `“${message.title}” has damaged frames: playing past them`
  toast(text, undefined, 10000, { action: { label: 'List damaged files', key: 'L', run: () => openSection('health') } })
})
