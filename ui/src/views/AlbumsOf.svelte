<script lang="ts">
  import { query } from '../bridge'
  import { app, addToPlaylist, playlist, albumItems, readSetting, writeSetting } from '../state.svelte'
  import { browseState, groupAlbums } from '../lib/browse.svelte'
  import AlbumList from '../parts/AlbumList.svelte'
  import BrowseBar from '../parts/BrowseBar.svelte'
  import PinButton from '../parts/PinButton.svelte'
  import PageHead from '../parts/PageHead.svelte'
  import { downloads } from '../lib/downloads.svelte'
  let { kind, key = '', title = '', params = {} }: { kind: string; key?: string; title?: string; params?: Record<string, unknown> } = $props()
  let result = $state<any>({ own: [], appearsOn: [] })
  // New music has three lists, each with its own grouping: newest added (Lyrion's New Music), newest changed files (Lyrion's Recently Updated) and a random pick
  const newModes: [string, string, string][] = [['added', 'Added', 'newAlbums'], ['updated', 'Updated', 'updatedAlbums'], ['random', 'Random', 'randomAlbums']]
  let newMode = $state(readSetting('newMusicMode', 'added')), shuffles = $state(0)
  const setNewMode = (mode: string) => { newMode = mode; writeSetting('newMusicMode', mode) }
  const ownView = browseState(kind), newViews: Record<string, ReturnType<typeof browseState>> = kind === 'new' ? { added: ownView, updated: browseState('updated'), random: browseState('random') } : {}
  const view = $derived(newViews[newMode] ?? ownView)
  const queryName: Record<string, string> = { artist: 'artistAlbums', composer: 'composerAlbums', performer: 'performerAlbums', genre: 'genreAlbums', albums: 'albums', downloads: 'downloadedAlbums', custom: 'customAlbums', new: 'newAlbums', compilations: 'compilations' }
  $effect(() => { app.indexVersion; if (kind === 'downloads') downloads.finishedTracks; app.librarySettings.AlbumSource; app.librarySettings.ContributorGrouping; shuffles; query(kind === 'new' ? (newModes.find(mode => mode[0] === newMode) ?? newModes[0])[2] : queryName[kind], Object.assign({ key, sort: view.sort }, params)).then(data => result = data ?? { own: [] }) })
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
<PageHead title={title || result.name || ''} detail={albumCount ? `${albumCount} ${albumCount === 1 ? 'album' : 'albums'}` : ''}>
  {#snippet titleExtra()}{#if kind !== 'albums' && kind !== 'custom' && key}<PinButton view={{ kind, key, title: title || result.name || '' }} />{/if}{/snippet}
  {#if playlist.open && (result.own ?? []).length}<button class="ghost" onclick={addAll}>＋ Add all to {playlist.name}</button>{/if}
  {#if kind === 'new' && newMode === 'random'}<button class="ghost" onclick={() => shuffles++}>Shuffle again</button>{/if}
  <BrowseBar {view} showSort={kind !== 'new'} />
</PageHead>
{#if kind === 'new'}<div class="chips">{#each newModes as [mode, label]}<button class:on={newMode === mode} onclick={() => setNewMode(mode)}>{label}</button>{/each}</div>{/if}
{#each sections as [sectionName, albums] (sectionName)}
  {@const groups = groupAlbums(albums, view.group)}
  {#if sectionName && groups.length > 1 || sectionName && view.group !== 'none'}<h3 class="section-name">{sectionName}</h3>{/if}
  <div class="groups" style:--card-width="{cardWidth}px" style:--column-gap="{columnGap}px">{#each groups as group (group.key)}<AlbumList albums={group.albums} section={kind} heading={group.heading || sectionName} size={view.size} minor={!!sectionName && !!group.heading} columns={0} />{/each}</div>
{/each}

<style>
  .groups { display: flex; flex-wrap: wrap; column-gap: var(--column-gap); align-items: flex-start; }
  .page-width { height: 0; }
  .section-name { margin: 24px 0 0; font-size: 17px; }
</style>
