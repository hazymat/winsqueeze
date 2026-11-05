<script lang="ts">
  import Dropdown from './Dropdown.svelte'
  import { sortOptions, sizeOptions, offeredGroups } from '../lib/browse.svelte'
  // Whole-view toolbar: Sort, Group by and View apply to every album list below it
  let { view, showSort = true }: { view: { sort: string; group: string; size: string }; showSort?: boolean } = $props()
</script>

<div class="browse-bar">
  {#if showSort}<Dropdown label="Sort" value={view.sort} options={sortOptions} onchange={(value) => view.sort = value} />{/if}
  <Dropdown label="Group" value={view.group} options={offeredGroups()} onchange={(value) => view.group = value} />
  <Dropdown label="View" value={view.size} options={sizeOptions} onchange={(value) => view.size = value} />
</div>

<style>
  .browse-bar { display: flex; gap: 6px; flex-wrap: wrap; justify-content: flex-end; }
</style>
