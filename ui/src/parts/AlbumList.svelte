<script lang="ts">
  import { selectable } from '../lib/selectable.svelte'
  import { browseActions } from '../lib/browseActions'
  import AlbumCard from './AlbumCard.svelte'
  import { ui } from '../state.svelte'
  // size comes from the view's toolbar (Large / Compact / List); minor headings sit under a section heading when grouped
  let { albums, section = 'albums', heading = '', size = 'large', minor = false }: { albums: any[]; section?: string; heading?: string; size?: string; minor?: boolean } = $props()
  const layout = $derived(size === 'list' ? 'list' : 'grid')
  // "This list" search filters the albums in view
  const shown = $derived(ui.scope === 'list' && ui.search.length >= 2 ? albums.filter(album => `${album.title} ${album.artist}`.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').includes(ui.search.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, ''))) : albums)
</script>

{#if albums.length}
  <div class="list-head" class:minor>
    {#if heading}<h3>{heading}</h3>{/if}
    <span class="count">{shown.length} {shown.length === 1 ? 'album' : 'albums'}</span>
  </div>
  <div class={layout === 'grid' ? 'grid ' + size : 'rows'} use:selectable={{ scope: 'albums:' + section + ':' + heading, noun: 'album', actions: browseActions('album') }}>
    {#each shown as album (album.key)}<div class="select-wrap" data-select-key={album.key}><AlbumCard {album} {layout} compact={size === 'compact'} /></div>{/each}
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
</style>
