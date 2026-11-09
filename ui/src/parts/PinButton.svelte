<script lang="ts">
  import { t } from '../lib/i18n'
  import Icon from './Icon.svelte'
  import { pins, pinKey, togglePin, type View } from '../state.svelte'
  // Pin this page to the sidebar (or unpin it)
  let { view }: { view: View } = $props()
  const pinned = $derived(!!pins.views[pinKey(view)])
</script>

<button class="pin-page" class:on={pinned} onclick={() => togglePin(view)} title={pinned ? t('Unpin from the sidebar') : t('Pin to the sidebar')} aria-pressed={pinned}><Icon name="pin" size={15} />{pinned ? t('Pinned') : t('Pin')}</button>

<style>
  .pin-page { display: inline-flex; align-items: center; gap: 5px; height: 28px; padding: 0 11px 0 9px; background: var(--panel); color: var(--fg-quiet); border: 1px solid var(--line); border-radius: 999px; font: inherit; font-size: 12px; cursor: pointer; white-space: nowrap; flex: none; }
  .pin-page:hover { background: var(--hover); color: var(--fg); }
  .pin-page.on { color: var(--accent); background: var(--accent-soft); border-color: transparent; }
</style>
