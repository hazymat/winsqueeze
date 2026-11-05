<script lang="ts">
  import { query } from '../bridge'
  import { app, readSetting, writeSetting, addToPlaylist, playlist, albumItems } from '../state.svelte'
  import AlbumList from '../parts/AlbumList.svelte'
  let { kind, key = '', title = '' }: { kind: string; key?: string; title?: string } = $props()
  let result = $state<any>({ own: [], appearsOn: [] })
  let sort = $state(readSetting('sort.' + kind, 'artist'))
  const queryName: Record<string, string> = { artist: 'artistAlbums', composer: 'composerAlbums', performer: 'performerAlbums', genre: 'genreAlbums', albums: 'albums' }
  $effect(() => { app.indexVersion; app.librarySettings.AlbumSource; app.librarySettings.ContributorGrouping; query(queryName[kind], { key, sort }).then(data => result = data ?? { own: [] }) })
  // Genres: albums under album-artist headings
  const genreGroups = $derived(kind === 'genre' ? Object.entries((result.own ?? []).reduce((groups: Record<string, any[]>, album: any) => { (groups[album.artist] ??= []).push(album); return groups }, {})) : [])
  const addAll = async () => { for (const album of result.own ?? []) addToPlaylist(await albumItems(album.key), album.title) }
</script>

<div class="head">
  <h2>{title || result.name || ''}</h2>
  <select bind:value={sort} onchange={() => writeSetting('sort.' + kind, sort)} aria-label="Sort">
    <option value="artist">Album artist, year</option><option value="title">Title</option><option value="year">Year</option><option value="added">Date added</option>
  </select>
  {#if playlist.open && (result.own ?? []).length}<button class="ghost" onclick={addAll}>＋ Add all to {playlist.name}</button>{/if}
</div>
{#if kind === 'genre'}
  {#each genreGroups as [artistName, albums] (artistName)}<AlbumList albums={albums as any[]} section={kind} heading={artistName} />{/each}
{:else}
  <AlbumList albums={result.own ?? []} section={kind} heading={kind === 'artist' ? 'Albums' : ''} />
  {#if (result.appearsOn ?? []).length}<AlbumList albums={result.appearsOn} section={kind} heading="Also appears on" />{/if}
{/if}

<style>
  .head { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
  h2 { margin: 0; flex: 1; min-width: 0; font-size: 24px; }
</style>
