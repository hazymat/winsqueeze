<script lang="ts">
  import { selectable } from '../lib/selectable.svelte'
  import { browseActions } from '../lib/browseActions'
  import { query, sendToShell, artUrl } from '../bridge'
  import { app, ui, openView, playlist, addToPlaylist, inPlaylist, formatTime } from '../state.svelte'
  import AlbumList from '../parts/AlbumList.svelte'
  import { isDamaged } from '../lib/damaged.svelte'
  let { searchBox }: { searchBox?: HTMLInputElement } = $props()
  let results = $state<any>({})
  let resultsBox = $state<HTMLDivElement>()
  // Keyboard: arrows move between results (Up/Down by row, so grids work), Enter plays or opens, Esc goes back to the search box
  const resultSelector = '.chips button, .card, .row, .group-head, .track .title'
  export function focusFirstResult() { resultsBox?.querySelector<HTMLElement>(resultSelector)?.focus() }
  function moveFocus(event: KeyboardEvent) {
    if (event.ctrlKey || event.altKey || !resultsBox) return
    if (event.key === 'Escape') { event.preventDefault(); searchBox?.focus(); searchBox?.select(); return }
    if (!['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(event.key)) return
    const items = Array.from(resultsBox.querySelectorAll<HTMLElement>(resultSelector)); const currentIndex = items.indexOf(document.activeElement as HTMLElement)
    if (currentIndex < 0) return
    event.preventDefault()
    let target: HTMLElement | undefined
    if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') target = items[currentIndex + (event.key === 'ArrowRight' ? 1 : -1)]
    else {
      const goingDown = event.key === 'ArrowDown'; const currentBox = items[currentIndex].getBoundingClientRect(); const currentCentre = currentBox.left + currentBox.width / 2
      const rowCandidates = items.map(item => ({ item, box: item.getBoundingClientRect() })).filter(entry => goingDown ? entry.box.top >= currentBox.bottom - 2 : entry.box.bottom <= currentBox.top + 2)
      if (rowCandidates.length) {
        const nearestRowEdge = goingDown ? Math.min.apply(null, rowCandidates.map(entry => entry.box.top)) : Math.max.apply(null, rowCandidates.map(entry => entry.box.bottom))
        const sameRow = rowCandidates.filter(entry => Math.abs((goingDown ? entry.box.top : entry.box.bottom) - nearestRowEdge) < 8)
        target = sameRow.reduce((best, entry) => Math.abs(entry.box.left + entry.box.width / 2 - currentCentre) < Math.abs(best.box.left + best.box.width / 2 - currentCentre) ? entry : best).item
      }
      else if (!goingDown) { searchBox?.focus(); return }
    }
    target?.focus(); target?.scrollIntoView({ block: 'nearest' })
  }
  let timer = 0
  // Debounced ~150 ms, served entirely from the local index
  $effect(() => { const text = ui.search; app.indexVersion; app.librarySettings.AlbumSource; clearTimeout(timer); timer = window.setTimeout(() => query('search', { text }).then(data => results = data ?? {}), 150) })
  const folderMode = $derived(app.librarySettings.AlbumSource !== 'tags')
  const personGroups = $derived([['Artists', 'artist', results.artists], ['Composers', 'composer', results.composers], ['Performers', 'performer', results.performers]].filter(group => (group[2] as any[] ?? []).length))
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div class="results" bind:this={resultsBox} onkeydown={moveFocus}>
{#each personGroups as [heading, kind, people] (heading)}
  <h3>{heading}</h3>
  <div class="chips">{#each people as person (person.key)}<button onclick={() => openView({ kind: kind as string, key: person.key, title: person.name })}>{person.name}{kind === 'performer' && person.detail ? ` · ${person.detail}` : ''}</button>{/each}</div>
{/each}
{#if (results.bestArtistAlbums ?? []).length}<AlbumList albums={results.bestArtistAlbums} section="search" heading={`${results.artists?.[0]?.name}: albums`} />{/if}
{#if (results.albums ?? []).length}<AlbumList albums={results.albums.map((hit: any) => ({ key: hit.key, title: hit.name, artist: hit.detail, cover: hit.cover, year: hit.year }))} section="search" heading={folderMode ? 'Folders' : 'Albums'} />{/if}
{#if (results.trackGroups ?? []).length}
  <h3>Tracks{folderMode ? ', by folder' : ''}</h3>
  <div use:selectable={{ scope: 'search:tracks', noun: 'track', actions: browseActions('track') }}>
  {#each results.trackGroups as group (group.album.key)}
    <div class="group">
      <button class="group-head" onclick={() => openView({ kind: 'album', key: group.album.key, title: group.album.title })}>
        {#if group.album.cover}<img src={artUrl(group.album.cover, 80)} alt="" loading="lazy" />{/if}
        <span><strong>{group.album.title}</strong><span class="quiet"> · {group.album.artist}{group.album.year ? ` · ${group.album.year}` : ''}</span></span>
      </button>
      {#each group.tracks as track (track.id)}
        <div class="track" data-select-key={String(track.id)}>
          <button class="title" onclick={() => sendToShell({ type: 'playTracks', ids: [track.id], mode: 'load' })}>{track.title}{#if isDamaged(track.id)}<span class="damaged-mark" title="Damaged file: playback may skip">⚠</span>{/if}{#if track.composer}<span class="quiet"> · {track.composer}</span>{/if}</button>
          <span class="quiet">{formatTime(track.duration)}</span>
          {#if playlist.open}<button class="add" onclick={() => addToPlaylist([{ id: track.id, title: track.title, artist: track.artist ?? '', duration: track.duration }], track.title)}>{inPlaylist(track.id) ? '✓' : '＋'}</button>{/if}
        </div>
      {/each}
    </div>
  {/each}
  </div>
{/if}
</div>
{#if ui.search.length >= 2 && !personGroups.length && !(results.albums ?? []).length && !(results.trackGroups ?? []).length}<p class="quiet">No matches for “{ui.search}”.</p>{/if}

<style>
  .results :global(:is(.chips button, .card, .row, .group-head, .track .title):focus-visible) { outline: 2px solid var(--accent); outline-offset: 2px; border-radius: 6px; }
  h3 { margin: 18px 0 8px; font-size: 15px; }
  .chips { display: flex; flex-wrap: wrap; gap: 6px; }
  .chips button { border: 1px solid var(--line); background: var(--panel); color: var(--fg); border-radius: 14px; padding: 5px 12px; cursor: pointer; font: inherit; font-size: 13px; }
  .chips button:hover { border-color: var(--accent); }
  .group { margin-bottom: 12px; background: var(--panel); border-radius: 10px; padding: 8px; }
  .group-head { display: flex; align-items: center; gap: 10px; background: none; border: 0; color: var(--fg); cursor: pointer; font: inherit; padding: 2px; text-align: left; }
  .group-head img { width: 36px; height: 36px; border-radius: 5px; object-fit: cover; }
  .track { display: flex; align-items: center; gap: 10px; padding: 4px 4px 4px 48px; }
  .title { flex: 1; min-width: 0; text-align: left; background: none; border: 0; color: var(--fg); cursor: pointer; font: inherit; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .title:hover { color: var(--accent); }
  .add { background: none; border: 0; color: var(--accent); cursor: pointer; }
  .quiet { color: var(--fg-quiet); font-size: 12px; }
</style>
