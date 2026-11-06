<script lang="ts">
  import Dropdown from './Dropdown.svelte'
  import Icon from './Icon.svelte'
  import { sortOptions, sizeOptions, offeredGroups } from '../lib/browse.svelte'
  // Whole-view toolbar: Sort, Group by and View apply to every album list below it
  let { view, showSort = true }: { view: { sort: string; group: string; size: string }; showSort?: boolean } = $props()
</script>

<div class="browse-bar">
  {#if showSort}<Dropdown label="Sort" value={view.sort} options={sortOptions} onchange={(value) => view.sort = value} />{/if}
  <Dropdown label="Group" value={view.group} options={offeredGroups()} onchange={(value) => view.group = value} />
  <div class="view-toggle" role="radiogroup" aria-label="View">
    {#each sizeOptions as [sizeValue, sizeLabel]}<button role="radio" aria-checked={view.size === sizeValue} class:on={view.size === sizeValue} title={sizeLabel} aria-label={sizeLabel} onclick={() => view.size = sizeValue}><Icon name={sizeValue === 'large' ? 'grid-large' : sizeValue === 'compact' ? 'grid-small' : 'list'} size={15} /></button>{/each}
  </div>
</div>

<style>
  .browse-bar { display: flex; gap: 6px; flex-wrap: wrap; justify-content: flex-end; }
  .view-toggle { display: flex; height: 30px; padding: 2px; gap: 2px; box-sizing: border-box; background: var(--panel); border: 1px solid var(--line); border-radius: 999px; }
  .view-toggle button { display: grid; place-items: center; width: 30px; height: 100%; padding: 0; background: none; border: 0; border-radius: 999px; color: var(--fg-quiet); cursor: pointer; }
  .view-toggle button:hover { background: var(--hover); color: var(--fg); }
  .view-toggle button.on { background: var(--accent-soft); color: var(--accent); }
</style>
