<script lang="ts">
  import { t } from '../lib/i18n'
  import { artUrl, sendToShell } from '../bridge'
  import { openView, playlist, addToPlaylist, albumItems } from '../state.svelte'
  import { downloads } from '../lib/downloads.svelte'
  import Icon from './Icon.svelte'
  let { album, layout = 'grid', compact = false }: { album: any; layout?: string; compact?: boolean } = $props()
  const downloaded = $derived(downloads.albums[album.key])
  // "Artist - Title" folder names: the artist is already on the line below
  const shortTitle = $derived(album.artist && String(album.title).toLowerCase().startsWith(String(album.artist).toLowerCase() + ' - ') ? String(album.title).slice(String(album.artist).length + 3) : album.title)
  const open = () => openView({ kind: 'album', key: album.key, title: album.title })
  const play = (event: Event) => { event.stopPropagation(); sendToShell({ type: 'playAlbum', key: album.key, mode: 'load' }) }
  const add = async (event: Event) => { event.stopPropagation(); addToPlaylist(await albumItems(album.key), album.title) }
</script>

{#if layout === 'grid'}
  <div class="card" class:compact role="button" tabindex="0" onclick={open} onkeydown={(event) => event.key === 'Enter' && open()}>
    <div class="art">
      {#if album.cover}<img src={artUrl(album.cover, 300)} alt="" loading="lazy" crossorigin="anonymous" />{:else}<div class="no-art">♪</div>{/if}
      <button class="play" onclick={play} aria-label={t('Play')}>▶</button>
      {#if playlist.open}<button class="add" onclick={add} aria-label={t('Add to playlist')}>＋</button>{/if}
      {#if downloaded}<span class="downloaded" class:partial={downloaded === 'partial'} title={downloaded === 'full' ? t('Downloaded to this PC') : t('Partly downloaded to this PC')}><Icon name="download" size={12} /></span>{/if}
    </div>
    <div class="title">{shortTitle}</div>
    <div class="sub">{album.artist}{album.year ? ` · ${album.year}` : ''}</div>
  </div>
{:else}
  <div class="row" role="button" tabindex="0" onclick={open} onkeydown={(event) => event.key === 'Enter' && open()}>
    {#if album.cover}<img src={artUrl(album.cover, 80)} alt="" loading="lazy" />{:else}<div class="no-art small">♪</div>{/if}
    <div class="text"><div class="title">{album.title}{#if downloaded}<span class="downloaded inline" class:partial={downloaded === 'partial'} title={t('Downloaded to this PC')}><Icon name="download" size={11} /></span>{/if}</div><div class="sub">{album.artist}{album.year ? ` · ${album.year}` : ''}{album.format ? ` · ${album.format}` : ''}</div></div>
    <button class="ghost" onclick={play}>{t('Play')}</button>
    {#if playlist.open}<button class="ghost" onclick={add}>{t('＋ Add')}</button>{/if}
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
  .title { margin-top: 8px; font-weight: 600; font-size: 13px; overflow: hidden; display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 2; line-height: 1.25; overflow-wrap: anywhere; }
  .sub { color: var(--fg-quiet); font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .compact .art { border-radius: 7px; box-shadow: 0 2px 8px var(--shadow); }
  .compact .title { margin-top: 5px; font-size: 11.5px; } .compact .sub { font-size: 10.5px; }
  .compact .play, .compact .add { width: 30px; height: 30px; bottom: 6px; font-size: 11px; } .compact .play { right: 6px; } .compact .add { right: 40px; }
  .row { display: flex; align-items: center; gap: 12px; padding: 6px 8px; border-radius: 8px; cursor: pointer; }
  .row:hover { background: var(--hover); }
  .row img { width: 44px; height: 44px; border-radius: 6px; object-fit: cover; }
  .row .text { flex: 1; min-width: 0; }
  .row .title { margin-top: 0; }
  /* on this PC: a small badge on the cover (outline when only some tracks are) */
  .downloaded { position: absolute; top: 6px; right: 6px; width: 20px; height: 20px; border-radius: 50%; display: grid; place-items: center; background: var(--accent); color: var(--on-accent); box-shadow: 0 1px 4px var(--shadow); }
  .downloaded.partial { background: var(--panel-solid); color: var(--accent); border: 1px solid var(--accent); }
  .downloaded.inline { position: static; display: inline-grid; width: 16px; height: 16px; margin-left: 6px; vertical-align: -3px; box-shadow: none; }
</style>
