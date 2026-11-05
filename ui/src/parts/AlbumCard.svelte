<script lang="ts">
  import { artUrl, sendToShell } from '../bridge'
  import { openView, playlist, addToPlaylist, albumItems } from '../state.svelte'
  let { album, layout = 'grid' }: { album: any; layout?: string } = $props()
  const open = () => openView({ kind: 'album', key: album.key, title: album.title })
  const play = (event: Event) => { event.stopPropagation(); sendToShell({ type: 'playAlbum', key: album.key, mode: 'load' }) }
  const add = async (event: Event) => { event.stopPropagation(); addToPlaylist(await albumItems(album.key), album.title) }
</script>

{#if layout === 'grid'}
  <div class="card" role="button" tabindex="0" onclick={open} onkeydown={(event) => event.key === 'Enter' && open()}>
    <div class="art">
      {#if album.cover}<img src={artUrl(album.cover, 300)} alt="" loading="lazy" crossorigin="anonymous" />{:else}<div class="no-art">♪</div>{/if}
      <button class="play" onclick={play} aria-label="Play">▶</button>
      {#if playlist.open}<button class="add" onclick={add} aria-label="Add to playlist">＋</button>{/if}
    </div>
    <div class="title">{album.title}</div>
    <div class="sub">{album.artist}{album.year ? ` · ${album.year}` : ''}</div>
  </div>
{:else}
  <div class="row" role="button" tabindex="0" onclick={open} onkeydown={(event) => event.key === 'Enter' && open()}>
    {#if album.cover}<img src={artUrl(album.cover, 80)} alt="" loading="lazy" />{:else}<div class="no-art small">♪</div>{/if}
    <div class="text"><div class="title">{album.title}</div><div class="sub">{album.artist}{album.year ? ` · ${album.year}` : ''}{album.format ? ` · ${album.format}` : ''}</div></div>
    <button class="ghost" onclick={play}>Play</button>
    {#if playlist.open}<button class="ghost" onclick={add}>＋ Add</button>{/if}
  </div>
{/if}

<style>
  .card { cursor: pointer; min-width: 0; }
  .art { position: relative; aspect-ratio: 1; border-radius: 10px; overflow: hidden; background: var(--panel-strong); box-shadow: 0 4px 16px var(--shadow); }
  .art img { width: 100%; height: 100%; object-fit: cover; display: block; }
  .no-art { width: 100%; height: 100%; display: grid; place-items: center; color: var(--fg-quiet); font-size: 40px; }
  .no-art.small { width: 44px; height: 44px; font-size: 18px; border-radius: 6px; background: var(--panel-strong); }
  .play, .add { position: absolute; bottom: 8px; width: 38px; height: 38px; border-radius: 50%; border: 0; background: var(--accent); color: var(--on-accent); opacity: 0; transition: opacity 150ms; cursor: pointer; font-size: 14px; }
  .play { right: 8px; } .add { right: 52px; background: var(--panel-strong); color: var(--fg); }
  .card:hover .play, .card:hover .add, .card:focus-visible .play { opacity: 1; }
  .title { margin-top: 8px; font-weight: 600; font-size: 13px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .sub { color: var(--fg-quiet); font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .row { display: flex; align-items: center; gap: 12px; padding: 6px 8px; border-radius: 8px; cursor: pointer; }
  .row:hover { background: var(--hover); }
  .row img { width: 44px; height: 44px; border-radius: 6px; object-fit: cover; }
  .row .text { flex: 1; min-width: 0; }
  .row .title { margin-top: 0; }
</style>
