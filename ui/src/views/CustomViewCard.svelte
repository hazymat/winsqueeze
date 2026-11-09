<script lang="ts">
  import { t } from '../lib/i18n'
  import { app, openView } from '../state.svelte'
  import { query, artUrl } from '../bridge'
  import { levelNames } from '../lib/browse.svelte'
  // One custom view on its group page: a cover mosaic from inside it, its full name and steps, and its first step's biggest rows as chips that jump straight in
  import CoverMosaic from '../parts/CoverMosaic.svelte'
  // layout: cards (the full card), tiles (a square cover mosaic, name, album count) or list (a row with a small mosaic)
  let { view, layout = 'cards' }: { view: any; layout?: string } = $props()
  let albums = $state<any[]>([]), rows = $state<any[]>([])
  const firstLevel = $derived(view.Levels?.[0] ?? '')
  $effect(() => {
    app.indexVersion; app.librarySettings.AlbumSource
    const scope = { genres: view.Genres ?? [], filters: [] }
    query('customAlbums', Object.assign({ sort: 'added' }, scope)).then(result => albums = (result?.own ?? []))
    if (firstLevel) query('customLevel', Object.assign({ level: firstLevel }, scope)).then(result => rows = Array.isArray(result) ? result : [])
  })
  // Covers spread across the view rather than only the newest few
  const covers = $derived.by(() => { const withArt = albums.filter(album => album.cover), step = Math.max(1, Math.floor(withArt.length / 6)); return withArt.filter((album, position) => position % step === 0).slice(0, 6) })
  const topRows = $derived(rows.slice().sort((first, second) => Number(second.albums) - Number(first.albums)).slice(0, 8))
  const steps = $derived((view.Levels ?? []).map((name: string) => levelNames[name] ?? name).concat([t('Albums')]).join(' › '))
  const openWhole = () => openView({ kind: 'custom', key: view.Id, title: view.Name, filters: [] })
  const openRow = (row: any) => openView({ kind: 'custom', key: view.Id, title: row.name, filters: [{ kind: firstLevel, key: String(row.key), name: row.name }] })
</script>

{#if layout === 'list'}
<button class="list-row" onclick={openWhole}><span class="thumb"><CoverMosaic covers={covers.slice(0, 4).map(album => album.cover)} name={view.Name} size={120} /></span><span class="list-text"><span class="list-name">{view.Name}</span><span class="meta">{steps}</span></span><span class="list-count"><b>{albums.length}</b> {albums.length === 1 ? 'album' : 'albums'}</span></button>
{:else if layout === 'tiles'}
<button class="tile" onclick={openWhole} title={steps}><CoverMosaic covers={covers.slice(0, 4).map(album => album.cover)} name={view.Name} /><span class="tile-name">{view.Name}</span><span class="meta"><b>{albums.length}</b> {albums.length === 1 ? 'album' : 'albums'}</span></button>
{:else}
<article class="card">
  <button class="cover-button" onclick={openWhole} aria-label={t('Open {name}', { name: view.Name })}>
    <div class="mosaic">{#each covers as album (album.key)}<img src={artUrl(album.cover, 300)} alt="" loading="lazy" />{/each}{#each Array(Math.max(0, 6 - covers.length)) as _}<span class="blank"></span>{/each}</div>
  </button>
  <div class="text">
    <button class="name" onclick={openWhole}>{view.Name}</button>
    <div class="total"><b>{albums.length}</b> {albums.length === 1 ? 'album' : 'albums'}{#if firstLevel && rows.length} · {rows.length} {(levelNames[firstLevel] ?? firstLevel).toLowerCase()}{/if}</div>
    <div class="meta">{steps}</div>
    {#if topRows.length}
      <div class="chips">{#each topRows as row (row.key)}<button class="chip" onclick={() => openRow(row)}>{row.name}<span class="count">{row.albums}</span></button>{/each}{#if rows.length > topRows.length}<button class="chip more" onclick={openWhole}>{t('+{count} more', { count: rows.length - topRows.length })}</button>{/if}</div>
    {/if}
  </div>
</article>
{/if}

<style>
  .card { display: flex; flex-direction: column; border: 1px solid var(--line); border-radius: 14px; background: var(--panel); overflow: hidden; }
  .cover-button { border: 0; padding: 0; background: none; cursor: pointer; display: block; }
  .mosaic { display: grid; grid-template-columns: repeat(3, 1fr); aspect-ratio: 3 / 2; }
  .mosaic img, .blank { width: 100%; aspect-ratio: 1; object-fit: cover; display: block; }
  .blank { background: color-mix(in srgb, var(--fg) 6%, transparent); }
  .cover-button:hover .mosaic { filter: brightness(1.1); }
  .text { padding: 12px 14px 14px; display: flex; flex-direction: column; gap: 6px; }
  .name { background: none; border: 0; padding: 0; color: var(--fg); font: inherit; font-size: 18px; font-weight: 600; text-align: left; cursor: pointer; overflow-wrap: anywhere; }
  .name:hover { color: var(--accent); }
  .meta { color: var(--fg-quiet); font-size: 12px; }
  .total { color: var(--fg-quiet); font-size: 13px; } .total b { color: var(--fg); font-size: 22px; font-weight: 700; margin-right: 2px; }
  .tile { display: flex; flex-direction: column; gap: 3px; text-align: left; background: transparent; border: 0; color: var(--fg); padding: 6px; border-radius: 12px; cursor: pointer; font: inherit; min-width: 0; }
  .tile:hover { background: var(--hover); }
  .list-row { display: flex; align-items: center; gap: 12px; width: 100%; text-align: left; background: transparent; border: 0; border-radius: 10px; color: var(--fg); padding: 6px 8px; cursor: pointer; font: inherit; }
  .list-row:hover { background: var(--hover); }
  .thumb { width: 44px; flex: none; }
  .list-text { display: flex; flex-direction: column; gap: 2px; flex: 1; min-width: 0; }
  .list-name { font-weight: 600; overflow-wrap: anywhere; }
  .list-count { color: var(--fg-quiet); font-size: 12px; white-space: nowrap; } .list-count b { color: var(--fg); font-size: 15px; }
  .tile-name { margin-top: 5px; font-weight: 600; overflow-wrap: anywhere; }
  .tile .meta b { color: var(--fg); }
  .chips { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 4px; }
  .chip { display: inline-flex; align-items: center; gap: 6px; border: 1px solid var(--line); background: transparent; color: var(--fg); border-radius: 14px; padding: 3px 10px; font: inherit; font-size: 12px; cursor: pointer; }
  .chip:hover { background: var(--hover); }
  .count { color: var(--fg-quiet); }
  .more { color: var(--fg-quiet); }
</style>
