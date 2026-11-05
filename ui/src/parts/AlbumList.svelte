<script lang="ts">
  import { selectable } from '../lib/selectable.svelte'
  import { browseActions } from '../lib/browseActions'
  import AlbumCard from './AlbumCard.svelte'
  import { readSetting, writeSetting, ui } from '../state.svelte'
  let { albums, section = 'albums', heading = '' }: { albums: any[]; section?: string; heading?: string } = $props()
  let layout = $state(readSetting('layout.' + section, 'grid'))
  const setLayout = (value: string) => { layout = value; writeSetting('layout.' + section, value) }
  // "This list" search filters the albums in view
  const shown = $derived(ui.scope === 'list' && ui.search.length >= 2 ? albums.filter(album => `${album.title} ${album.artist}`.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').includes(ui.search.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, ''))) : albums)
</script>

{#if albums.length}
  <div class="list-head">
    {#if heading}<h3>{heading}</h3>{/if}
    <span class="count">{shown.length} {shown.length === 1 ? 'album' : 'albums'}</span>
    <div class="toggle"><button class:on={layout === 'grid'} onclick={() => setLayout('grid')} aria-label="Grid">▦</button><button class:on={layout === 'list'} onclick={() => setLayout('list')} aria-label="List">☰</button></div>
  </div>
  <div class={layout === 'grid' ? 'grid' : 'rows'} use:selectable={{ scope: 'albums:' + section + ':' + heading, noun: 'album', actions: browseActions('album') }}>
    {#each shown as album (album.key)}<div class="select-wrap" data-select-key={album.key}><AlbumCard {album} {layout} /></div>{/each}
  </div>
{/if}

<style>
  .list-head { display: flex; align-items: baseline; gap: 12px; margin: 18px 0 10px; }
  h3 { margin: 0; font-size: 15px; }
  .count { color: var(--fg-quiet); font-size: 12px; }
  .toggle { margin-left: auto; display: flex; gap: 2px; }
  .toggle button { background: transparent; border: 0; color: var(--fg-quiet); cursor: pointer; font-size: 16px; padding: 2px 6px; border-radius: 4px; }
  .toggle button.on { color: var(--fg); background: var(--hover); }
  .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 18px; }
  .rows { display: flex; flex-direction: column; gap: 2px; }
</style>
