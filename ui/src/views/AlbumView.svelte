<script lang="ts">
  import PinButton from '../parts/PinButton.svelte'
  import { selectable } from '../lib/selectable.svelte'
  import { browseActions } from '../lib/browseActions'
  import { query, sendToShell, live, artUrl } from '../bridge'
  import { app, openView, playlist, addToPlaylist, inPlaylist, formatTime, toast } from '../state.svelte'
  let { albumKey }: { albumKey: string } = $props()
  let data = $state<any>({ album: null, tracks: [] })
  let menuFor = $state<number | null>(null)
  $effect(() => { app.indexVersion; query('album', { key: albumKey }).then(result => data = result ?? { tracks: [] }) })
  const album = $derived(data.album)
  const discs = $derived<[string, any[]][]>(Object.entries((data.tracks ?? []).reduce((groups: Record<string, any[]>, track: any) => { (groups[track.disc || 1] ??= []).push(track); return groups }, {})))
  const multiComposer = $derived(new Set((data.tracks ?? []).map((track: any) => track.composer).filter(Boolean)).size > 1)
  const item = (track: any) => ({ id: track.id, title: track.title, artist: track.artist || track.albumArtist || '', duration: track.duration })
  const allAdded = $derived((data.tracks ?? []).length > 0 && (data.tracks ?? []).every((track: any) => inPlaylist(track.id)))
  const playAlbum = (mode: string, start = 0) => sendToShell({ type: 'playAlbum', key: albumKey, mode, start })
  const trackIndex = (track: any) => (data.tracks ?? []).indexOf(track)
  // Favourites go to Lyrion's own list, as the folder (one folder = one CD)
  async function favourite() {
    if (!data.folderPath) return
    const folderUrl = 'file://' + encodeURI(data.folderPath.startsWith('/') ? data.folderPath : '/' + data.folderPath.replace(/\\/g, '/')) + '/'
    await live(['favorites', 'add', `url:${folderUrl}`, `title:${album.title}`])
    toast(`Added ${album.title} to favourites`)
  }
  const goTo = (kind: string, name: string) => { if (name) openView({ kind, key: name.split(',')[0].trim().toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, ''), title: name.split(',')[0].trim() }) }
</script>

{#if album}
  <div class="hero">
    {#if album.cover}<img class="cover" src={artUrl(album.cover, 600)} alt="" />{:else}<div class="cover none">♪</div>{/if}
    <div class="info">
      <div class="kicker">{album.key.startsWith('f:') ? 'Folder album' : 'Album'}</div>
      <h1>{album.title}</h1><PinButton view={{ kind: 'album', key: albumKey, title: album.title }} />
      <button class="link" onclick={() => goTo('artist', album.artist)}>{album.artist}</button>
      <div class="meta">{album.year || ''}{album.year ? ' · ' : ''}{album.tracks} tracks · {formatTime(album.duration)}{album.format ? ` · ${album.format}` : ''}</div>
      <div class="actions">
        <button class="primary" onclick={() => playAlbum('load')}>▶ Play</button>
        <button class="ghost" onclick={() => { playAlbum('insert'); toast('Playing next') }}>Play next</button>
        <button class="ghost" onclick={() => { playAlbum('add'); toast('Added to queue') }}>Add to queue</button>
        <button class="ghost" onclick={favourite} title="Add to Lyrion favourites">☆ Favourite</button>
        {#if playlist.open}<button class="ghost" onclick={() => addToPlaylist((data.tracks ?? []).map(item), album.title)}>{allAdded ? '✓ Added' : `＋ Add to ${playlist.name}`}</button>{/if}
      </div>
    </div>
  </div>
  <div use:selectable={{ scope: 'album:' + albumKey, noun: 'track', actions: browseActions('track') }}>
  {#each discs as [discNo, tracks] (discNo)}
    {#if discs.length > 1}<h4>Disc {discNo}</h4>{/if}
    <div class="tracks">
      {#each tracks as track (track.id)}
        <div class="track" role="button" tabindex="0" ondblclick={() => playAlbum('load', trackIndex(track))} onkeydown={(event) => event.key === 'Enter' && playAlbum('load', trackIndex(track))} data-select-key={String(track.id)}>
          <button class="number" onclick={() => playAlbum('load', trackIndex(track))} aria-label="Play from here"><span class="digit">{track.number || ''}</span><span class="arrow">▶</span></button>
          <div class="text">
            <div class="title">{track.title}{#if playlist.open && inPlaylist(track.id)}<span class="added"> ✓</span>{/if}</div>
            {#if (album.key.startsWith('f:') && multiComposer && track.composer) || (track.artist && track.artist !== album.artist)}<div class="quiet-line">{[multiComposer ? track.composer : '', track.artist !== album.artist ? track.artist : ''].filter(Boolean).join(' · ')}</div>{/if}
          </div>
          <span class="duration">{formatTime(track.duration)}</span>
          {#if playlist.open}<button class="icon" onclick={() => addToPlaylist([item(track)], track.title)} aria-label="Add to playlist">＋</button>{/if}
          <button class="icon no-hold" onclick={() => menuFor = menuFor === track.id ? null : track.id} aria-label="More">⋯</button>
          {#if menuFor === track.id}
            <div class="menu no-hold" role="menu">
              <button onclick={() => { sendToShell({ type: 'playTracks', ids: [track.id], mode: 'insert' }); menuFor = null; toast('Playing next') }}>Play next</button>
              <button onclick={() => { sendToShell({ type: 'playTracks', ids: [track.id], mode: 'add' }); menuFor = null; toast('Added to queue') }}>Add to queue</button>
              <button onclick={() => goTo('artist', track.artist || album.artist)}>Go to artist</button>
              {#if track.composer}<button onclick={() => goTo('composer', track.composer)}>Go to composer</button>{/if}
              {#if album.folder}<button onclick={() => openView({ kind: 'folder', key: String(album.folder), title: album.title })}>Open folder</button>{/if}
              <button onclick={() => { if (!playlist.open) { menuFor = null; return } addToPlaylist([item(track)], track.title); menuFor = null }} disabled={!playlist.open}>Add to playlist</button>
            </div>
          {/if}
        </div>
      {/each}
    </div>
  {/each}
  </div>
{/if}

<svelte:window onclick={(event) => { if (!(event.target as HTMLElement).closest('.menu, .icon')) menuFor = null }} />

<style>
  .hero { display: flex; gap: 24px; align-items: flex-end; margin-bottom: 20px; flex-wrap: wrap; }
  .cover { width: 220px; height: 220px; border-radius: 12px; object-fit: cover; box-shadow: 0 12px 40px var(--shadow); background: var(--panel-strong); }
  .cover.none { display: grid; place-items: center; font-size: 60px; color: var(--fg-quiet); }
  .info { flex: 1; min-width: 240px; }
  .kicker { color: var(--fg-quiet); font-size: 12px; text-transform: uppercase; letter-spacing: 0.08em; }
  h1 { margin: 4px 0; font-size: 28px; line-height: 1.15; }
  .link { background: none; border: 0; padding: 0; color: var(--fg); font: inherit; font-size: 16px; cursor: pointer; }
  .link:hover { text-decoration: underline; }
  .meta { color: var(--fg-quiet); margin: 6px 0 14px; font-size: 13px; }
  .actions { display: flex; gap: 8px; flex-wrap: wrap; }
  h4 { margin: 18px 0 6px; color: var(--fg-quiet); font-weight: 600; font-size: 13px; }
  .tracks { display: flex; flex-direction: column; }
  .track { position: relative; display: flex; align-items: center; gap: 10px; padding: 6px 8px; border-radius: 8px; }
  .track:hover { background: var(--hover); }
  /* digit and arrow share one grid cell and swap visibility, so hover never changes the row height */
  .number { display: grid; justify-items: end; align-items: center; width: 28px; height: 20px; padding: 0; background: none; border: 0; color: var(--fg-quiet); cursor: pointer; font: inherit; line-height: 20px; }
  .number > span { grid-area: 1 / 1; }
  .number .arrow { visibility: hidden; color: var(--accent); }
  .track:hover .number .digit { visibility: hidden; }
  .track:hover .number .arrow { visibility: visible; }
  .text { flex: 1; min-width: 0; }
  .title { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .added { color: var(--accent); }
  .quiet-line { color: var(--fg-quiet); font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .duration { color: var(--fg-quiet); font-size: 12px; font-variant-numeric: tabular-nums; }
  .icon { background: none; border: 0; color: var(--fg-quiet); cursor: pointer; font-size: 16px; padding: 0 4px; }
  .icon:hover { color: var(--fg); }
  .menu { position: absolute; right: 8px; top: 34px; z-index: 20; background: var(--panel-solid); border: 1px solid var(--line); border-radius: 8px; padding: 4px; display: flex; flex-direction: column; min-width: 170px; box-shadow: 0 8px 24px var(--shadow); }
  .menu button { background: none; border: 0; color: var(--fg); text-align: left; padding: 7px 10px; border-radius: 6px; cursor: pointer; font: inherit; }
  .menu button:hover:not(:disabled) { background: var(--hover); }
  .menu button:disabled { color: var(--fg-quiet); }
</style>
