<script lang="ts">
  import { query } from '../bridge'
  import { app, addToPlaylist, playlist, albumItems } from '../state.svelte'
  import { browseState, groupAlbums } from '../lib/browse.svelte'
  import AlbumList from '../parts/AlbumList.svelte'
  import BrowseBar from '../parts/BrowseBar.svelte'
  import PinButton from '../parts/PinButton.svelte'
  import { downloads } from '../lib/downloads.svelte'
  let { kind, key = '', title = '' }: { kind: string; key?: string; title?: string } = $props()
  let result = $state<any>({ own: [], appearsOn: [] })
  const view = browseState(kind)
  const queryName: Record<string, string> = { artist: 'artistAlbums', composer: 'composerAlbums', performer: 'performerAlbums', genre: 'genreAlbums', albums: 'albums', downloads: 'downloadedAlbums' }
  $effect(() => { app.indexVersion; if (kind === 'downloads') downloads.finishedTracks; app.librarySettings.AlbumSource; app.librarySettings.ContributorGrouping; query(queryName[kind], { key, sort: view.sort }).then(data => result = data ?? { own: [] }) })
  // An artist keeps "Albums" and "Also appears on" as sections, with the chosen grouping inside each
  const sections = $derived(kind === 'artist' ? [['Albums', result.own ?? []], ['Also appears on', result.appearsOn ?? []]].filter(([, albums]) => (albums as any[]).length) as [string, any[]][] : [['', result.own ?? []]] as [string, any[]][])
  // Grouped grids share one column width (as auto-fill would give), so small groups can sit side by side on a row
  let pageWidth = $state(0)
  const columnGap = $derived(view.size === 'compact' ? 10 : 18), minCard = $derived(view.size === 'compact' ? 86 : 150)
  const columns = $derived(Math.max(1, Math.floor((pageWidth + columnGap) / (minCard + columnGap))))
  const cardWidth = $derived(pageWidth ? (pageWidth - (columns - 1) * columnGap) / columns : minCard)
  const albumCount = $derived(sections.reduce((total, [, albums]) => total + albums.length, 0))
  const addAll = async () => { for (const album of result.own ?? []) addToPlaylist(await albumItems(album.key), album.title) }
</script>

<div class="page-width" bind:clientWidth={pageWidth}></div>
<div class="head">
  <div class="titles"><h2>{title || result.name || ''}</h2>{#if albumCount}<span class="count">{albumCount} {albumCount === 1 ? 'album' : 'albums'}</span>{/if}{#if kind !== 'albums' && key}<PinButton view={{ kind, key, title: title || result.name || '' }} />{/if}</div>
  {#if playlist.open && (result.own ?? []).length}<button class="ghost" onclick={addAll}>＋ Add all to {playlist.name}</button>{/if}
  <BrowseBar {view} />
</div>
{#each sections as [sectionName, albums] (sectionName)}
  {@const groups = groupAlbums(albums, view.group)}
  {#if sectionName && groups.length > 1 || sectionName && view.group !== 'none'}<h3 class="section-name">{sectionName}</h3>{/if}
  <div class="groups" style:--card-width="{cardWidth}px" style:--column-gap="{columnGap}px">{#each groups as group (group.key)}<AlbumList albums={group.albums} section={kind} heading={group.heading || sectionName} size={view.size} minor={!!sectionName && !!group.heading} columns={groups.length > 1 ? columns : 0} />{/each}</div>
{/each}

<style>
  /* title, count and Sort / Group / View stay pinned while the albums scroll under them */
  .head { position: sticky; top: -4px; z-index: 6; display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin: 0 -20px; padding: 8px 20px; background: var(--glass); backdrop-filter: blur(var(--blur)) saturate(1.4); border-bottom: 1px solid var(--line); }
  .titles { flex: 1; min-width: 0; display: flex; align-items: baseline; gap: 10px; }
  h2 { margin: 0; font-size: 24px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .count { color: var(--fg-quiet); font-size: 12px; white-space: nowrap; }
  .groups { display: flex; flex-wrap: wrap; column-gap: var(--column-gap); align-items: flex-start; }
  .page-width { height: 0; }
  .section-name { margin: 24px 0 0; font-size: 17px; }
</style>
