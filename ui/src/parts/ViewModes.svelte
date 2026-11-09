<script lang="ts">
  import { t } from '../lib/i18n'
  import Icon from './Icon.svelte'
  // The view switcher at the right of every browse page's title bar: List, Cards (taller rows with a cover) and Tiles (cover mosaics)
  let { mode, onchange, modes = ['list', 'cards', 'tiles'] }: { mode: string; onchange: (mode: string) => void; modes?: string[] } = $props()
  const labels: Record<string, string> = { list: t('List'), cards: t('Cards'), tiles: t('Tiles') }
  const icons: Record<string, 'list' | 'cards' | 'grid-large'> = { list: 'list', cards: 'cards', tiles: 'grid-large' }
</script>

<div class="view-toggle" role="radiogroup" aria-label={t('View')}>
  {#each modes as value}<button role="radio" aria-checked={mode === value} class:on={mode === value} title={labels[value]} aria-label={labels[value]} onclick={() => onchange(value)}><Icon name={icons[value]} size={15} /></button>{/each}
</div>

<style>
  .view-toggle { display: flex; height: 30px; padding: 2px; gap: 2px; box-sizing: border-box; background: var(--panel); border: 1px solid var(--line); border-radius: 999px; flex: none; }
  .view-toggle button { display: grid; place-items: center; width: 30px; height: 100%; padding: 0; background: none; border: 0; border-radius: 999px; color: var(--fg-quiet); cursor: pointer; }
  .view-toggle button:hover { background: var(--hover); color: var(--fg); }
  .view-toggle button.on { background: var(--accent-soft); color: var(--accent); }
</style>
