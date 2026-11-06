<script lang="ts">
  import { selectable } from '../lib/selectable.svelte'
  import { browseActions } from '../lib/browseActions'
  import AlbumCard from './AlbumCard.svelte'
  import { ui } from '../state.svelte'
  // size comes from the view's toolbar (Large / Compact / List); minor headings sit under a section heading when grouped
  let { albums, section = 'albums', heading = '', size = 'large', minor = false, columns = 0 }: { albums: any[]; section?: string; heading?: string; size?: string; minor?: boolean; columns?: number } = $props()
  const layout = $derived(size === 'list' ? 'list' : 'grid')
  // columns: set when grouped groups share rows; a group smaller than a row takes just its own columns and sits beside the next one
  const packed = $derived(layout === 'grid' && columns > 0 && albums.length < columns)
  // "This list" search filters the albums in view
  const shown = $derived(ui.scope === 'list' && ui.search.length >= 2 ? albums.filter(album => `${album.title} ${album.artist}`.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').includes(ui.search.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, ''))) : albums)
</script>

{#if albums.length}
<div class="group" class:packed class:shared={columns > 0 && layout === 'grid'} style:--span={packed ? shown.length || albums.length : null}>
  <div class="list-head" class:minor>
    {#if heading}<h3 title={heading}>{heading}</h3>{/if}
    <span class="count">{shown.length} {shown.length === 1 ? 'album' : 'albums'}</span>
  </div>
  <div class={layout === 'grid' ? 'grid ' + size : 'rows'} use:selectable={{ scope: 'albums:' + section + ':' + heading, noun: 'album', actions: browseActions('album') }}>
    {#each shown as album (album.key)}<div class="select-wrap" data-select-key={album.key}><AlbumCard {album} {layout} compact={size === 'compact'} /></div>{/each}
  </div>
</div>
{/if}

<style>
  .list-head { display: flex; align-items: baseline; gap: 12px; margin: 18px 0 10px; }
  h3 { margin: 0; font-size: 15px; }
  .count { color: var(--fg-quiet); font-size: 12px; }
  .list-head.minor { margin: 14px 0 8px; } .minor h3 { font-size: 13px; color: var(--fg-soft, var(--fg)); }
  .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 18px; }
  .grid.compact { grid-template-columns: repeat(auto-fill, minmax(86px, 1fr)); gap: 12px 10px; }
  .rows { display: flex; flex-direction: column; gap: 2px; }
  /* chosen covers: an accent ring outside the art and a tick (a tinted background would hide behind the cover); the rest dim */
  .grid :global(.select-wrap.selected) { background: none; box-shadow: none; }
  .grid :global(.select-wrap.selected .art) { outline: 3px solid var(--accent); outline-offset: 3px; }
  .grid :global(.select-wrap.selected .art::after) { content: "✓"; position: absolute; top: 6px; right: 6px; width: 24px; height: 24px; border-radius: 50%; display: grid; place-items: center; background: var(--accent); color: var(--on-accent); font-size: 14px; font-weight: 700; box-shadow: 0 2px 6px rgba(0, 0, 0, 0.4); }
  :global(:is(.groups, .grid):has(.select-wrap.selected) .select-wrap:not(.selected) > *) { opacity: 0.5; transition: opacity 150ms; }
  /* shared rows: every grid uses the page's exact column width, so packed groups line up with full rows */
  .group { min-width: 0; flex: 1 1 100%; }
  .group.packed { flex: 0 0 calc(var(--span) * var(--card-width) + (var(--span) - 1) * var(--column-gap)); }
  .shared .grid { grid-template-columns: repeat(auto-fill, var(--card-width)); column-gap: var(--column-gap); }
  .packed h3 { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; min-width: 0; } .packed .list-head { min-width: 0; } .packed .count { white-space: nowrap; }
</style>
