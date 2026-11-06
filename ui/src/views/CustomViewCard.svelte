<script lang="ts">
  import { app, openView } from '../state.svelte'
  import { query, artUrl } from '../bridge'
  import { levelNames } from '../lib/browse.svelte'
  // One custom view on its group page: a cover mosaic from inside it, its full name and steps, and its first step's biggest rows as chips that jump straight in
  let { view }: { view: any } = $props()
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
  const steps = $derived((view.Levels ?? []).map((name: string) => levelNames[name] ?? name).concat(['Albums']).join(' › '))
  const openWhole = () => openView({ kind: 'custom', key: view.Id, title: view.Name, filters: [] })
  const openRow = (row: any) => openView({ kind: 'custom', key: view.Id, title: row.name, filters: [{ kind: firstLevel, key: String(row.key), name: row.name }] })
</script>

<article class="card">
  <button class="cover-button" onclick={openWhole} aria-label="Open {view.Name}">
    <div class="mosaic">{#each covers as album (album.key)}<img src={artUrl(album.cover, 300)} alt="" loading="lazy" />{/each}{#each Array(Math.max(0, 6 - covers.length)) as _}<span class="blank"></span>{/each}</div>
  </button>
  <div class="text">
    <button class="name" onclick={openWhole}>{view.Name}</button>
    <div class="meta">{steps}{#if albums.length} · {albums.length} {albums.length === 1 ? 'album' : 'albums'}{/if}{#if firstLevel && rows.length} · {rows.length} {(levelNames[firstLevel] ?? firstLevel).toLowerCase()}{/if}</div>
    {#if topRows.length}
      <div class="chips">{#each topRows as row (row.key)}<button class="chip" onclick={() => openRow(row)}>{row.name}<span class="count">{row.albums}</span></button>{/each}{#if rows.length > topRows.length}<button class="chip more" onclick={openWhole}>+{rows.length - topRows.length} more</button>{/if}</div>
    {/if}
  </div>
</article>

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
  .chips { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 4px; }
  .chip { display: inline-flex; align-items: center; gap: 6px; border: 1px solid var(--line); background: transparent; color: var(--fg); border-radius: 14px; padding: 3px 10px; font: inherit; font-size: 12px; cursor: pointer; }
  .chip:hover { background: var(--hover); }
  .count { color: var(--fg-quiet); }
  .more { color: var(--fg-quiet); }
</style>
