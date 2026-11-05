<script lang="ts">
  import { query } from '../bridge'
  import { app, addToPlaylist, playlist, albumItems } from '../state.svelte'
  import { browseState, groupAlbums } from '../lib/browse.svelte'
  import AlbumList from '../parts/AlbumList.svelte'
  import BrowseBar from '../parts/BrowseBar.svelte'
  let { kind, key = '', title = '' }: { kind: string; key?: string; title?: string } = $props()
  let result = $state<any>({ own: [], appearsOn: [] })
  const view = browseState(kind)
  const queryName: Record<string, string> = { artist: 'artistAlbums', composer: 'composerAlbums', performer: 'performerAlbums', genre: 'genreAlbums', albums: 'albums' }
  $effect(() => { app.indexVersion; app.librarySettings.AlbumSource; app.librarySettings.ContributorGrouping; query(queryName[kind], { key, sort: view.sort }).then(data => result = data ?? { own: [] }) })
  // An artist keeps "Albums" and "Also appears on" as sections, with the chosen grouping inside each
  const sections = $derived(kind === 'artist' ? [['Albums', result.own ?? []], ['Also appears on', result.appearsOn ?? []]].filter(([, albums]) => (albums as any[]).length) as [string, any[]][] : [['', result.own ?? []]] as [string, any[]][])
  const albumCount = $derived(sections.reduce((total, [, albums]) => total + albums.length, 0))
  const addAll = async () => { for (const album of result.own ?? []) addToPlaylist(await albumItems(album.key), album.title) }
</script>

<div class="head">
  <div class="titles"><h2>{title || result.name || ''}</h2>{#if albumCount}<span class="count">{albumCount} {albumCount === 1 ? 'album' : 'albums'}</span>{/if}</div>
  {#if playlist.open && (result.own ?? []).length}<button class="ghost" onclick={addAll}>＋ Add all to {playlist.name}</button>{/if}
  <BrowseBar {view} />
</div>
{#each sections as [sectionName, albums] (sectionName)}
  {@const groups = groupAlbums(albums, view.group)}
  {#if sectionName && groups.length > 1 || sectionName && view.group !== 'none'}<h3 class="section-name">{sectionName}</h3>{/if}
  {#each groups as group (group.key)}<AlbumList albums={group.albums} section={kind} heading={group.heading || sectionName} size={view.size} minor={!!sectionName && !!group.heading} />{/each}
{/each}

<style>
  .head { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
  .titles { flex: 1; min-width: 0; display: flex; align-items: baseline; gap: 10px; }
  h2 { margin: 0; font-size: 24px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .count { color: var(--fg-quiet); font-size: 12px; white-space: nowrap; }
  .section-name { margin: 24px 0 0; font-size: 17px; }
</style>
