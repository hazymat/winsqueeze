<script lang="ts">
  import { t, tn } from '../lib/i18n'
  import { selectable } from '../lib/selectable.svelte'
  import { browseActions } from '../lib/browseActions'
  import { query } from '../bridge'
  import { app, ui, openView, readSetting, writeSetting, type CustomStep } from '../state.svelte'
  import CoverMosaic from '../parts/CoverMosaic.svelte'
  import PageHead from '../parts/PageHead.svelte'
  import ViewModes from '../parts/ViewModes.svelte'
  import { artUrl } from '../bridge'
  import { browseState } from '../lib/browse.svelte'
  // custom: one level of a custom view (SPEC.md › Custom views); rows come from its scope, and a click takes the next step
  let { kind, title, detail = '', custom }: { kind: 'artists' | 'albumartists' | 'composers' | 'performers' | 'genres' | 'custom'; title: string; detail?: string; custom?: { viewId: string; genres: string[]; filters: CustomStep[]; level: string } } = $props()
  let rows = $state<any[]>([])
  let role = $state('')
  const roleNames: Record<string, string> = { '': t('All'), conductor: t('Conductors'), band: t('Bands & orchestras'), soloist: t('Soloists') }
  $effect(() => { app.indexVersion; app.librarySettings.AlbumSource; (custom ? query('customLevel', { genres: custom.genres, filters: custom.filters, level: custom.level }) : query(kind === 'albumartists' ? 'albumArtists' : kind, { role })).then(result => rows = Array.isArray(result) ? result : []) })
  const plain = (text: string) => (text ?? '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')
  // Matches from the start of any word: "jarr" finds Keith Jarrett
  const shown = $derived(ui.search.length >= 2 ? rows.filter(row => plain(row.name).split(/[\s,.&/-]+/).some(word => word.startsWith(plain(ui.search)))) : rows)
  const viewKind: string = custom ? custom.level : ({ artists: 'artist', albumartists: 'artist', composers: 'composer', performers: 'performer', genres: 'genre' } as Record<string, string>)[kind] // keyed per kind (or custom step) by the parent
  const stepTo = (row: any): CustomStep[] => custom!.filters.concat([{ kind: custom!.level, key: String(row.key), name: row.name }])
  // A custom row is selected (and played) as its whole scope: the view's genres plus every step to it
  const selectKey = (row: any) => custom ? JSON.stringify({ genres: custom.genres, filters: stepTo(row).map(step => ({ kind: step.kind, key: step.key })) }) : String(row.key)
  const open = (row: any) => custom ? openView({ kind: 'custom', key: custom.viewId, title: row.name, filters: stepTo(row) }) : openView({ kind: viewKind, key: String(row.key), title: row.name })
  // The count matches what the next view groups by (album artists, years…), or tracks, or both (Settings › Library)
  const nextView = custom ? { group: 'none' } : browseState(viewKind) // keyed per kind by the parent
  const groupCounts: Record<string, [string, (count: number) => string]> = { artist: ['artists', count => tn(count, '{count} album artist', '{count} album artists')], year: ['years', count => tn(count, '{count} year', '{count} years')], decade: ['decades', count => tn(count, '{count} decade', '{count} decades')], format: ['formats', count => tn(count, '{count} format', '{count} formats')] }
  function countText(row: any) {
    const [field, countLabel] = groupCounts[nextView.group] ?? ['albums', (count: number) => tn(count, '{count} album', '{count} albums')]
    const grouped = row[field] != null ? countLabel(Number(row[field])) : '', tracks = row.tracks != null ? tn(Number(row.tracks), '{count} track', '{count} tracks') : ''
    const mode = app.librarySettings.ListCounts ?? 'group'
    return mode === 'tracks' ? tracks || grouped : mode === 'both' ? [grouped, tracks].filter(Boolean).join(' · ') : grouped || tracks
  }
  // Size graphic (Settings › Library): log share of the biggest row, by tracks when only tracks are shown, else by the grouped count
  const graphic = $derived(app.librarySettings.ListSizeGraphic ?? 'none')
  const sizeOf = (row: any) => { const field = (groupCounts[nextView.group] ?? ['albums'])[0]; return Number((app.librarySettings.ListCounts === 'tracks' ? row.tracks : row[field] ?? row.tracks) ?? 0) }
  const biggest = $derived(graphic === 'none' ? 1 : rows.reduce((largest, row) => Math.max(largest, sizeOf(row)), 1))
  const shareOf = (row: any) => Math.log(sizeOf(row) + 1) / Math.log(biggest + 1)
  // List or Tiles (a cover mosaic per row), remembered per list
  const layoutKey = 'peopleLayout.' + (custom ? 'custom' : kind)
  let layout = $state(readSetting(layoutKey, 'list'))
  const setLayout = (value: string) => { layout = value; writeSetting(layoutKey, value) }
  const countParts = (row: any) => { const text = countText(row), space = text.indexOf(' '); return space > 0 ? [text.slice(0, space), text.slice(space)] : [text, ''] }
</script>

<PageHead {title} detail={detail || (rows.length ? shown.length === rows.length ? String(rows.length) : t('{shown} of {count}', { shown: shown.length, count: rows.length }) : '')}><ViewModes mode={layout} onchange={setLayout} /></PageHead>
{#if kind === 'performers'}<div class="chips">{#each Object.keys(roleNames) as roleKey}<button class:on={role === roleKey} onclick={() => role = roleKey}>{roleNames[roleKey]}</button>{/each}</div>{/if}
{#if !rows.length}<p class="quiet">{app.library.text || (custom ? t('Nothing in your library matches this view. Check its genres in Custom views.') : t('Nothing here yet. The library is still syncing, or the server has none.'))}</p>{/if}
<div class="people" class:tiles={layout === 'tiles'} class:cards={layout === 'cards'} use:selectable={{ scope: 'people:' + kind + (custom ? custom.viewId + custom.filters.length : ''), noun: viewKind ?? 'item', actions: browseActions(custom ? 'custom' : viewKind ?? 'artist') }}>
  {#each shown as row (row.key + (row.role ?? ''))}
    {@const share = graphic === 'none' ? 0 : shareOf(row)}
    {#if layout === 'tiles'}
      {@const [count, countRest] = countParts(row)}
      <button class="tile" data-select-key={selectKey(row)} onclick={() => open(row)} title={row.name}><CoverMosaic covers={row.covers ?? []} name={row.name} /><span class="tile-name">{row.name}</span><span class="meta"><b>{count}</b>{countRest}</span></button>
    {:else if layout === 'cards'}
      {@const [count, countRest] = countParts(row)}
      <button class="card-row" data-select-key={selectKey(row)} onclick={() => open(row)} title={row.name}>{#if row.covers?.length}<img src={artUrl(row.covers[0], 120)} alt="" loading="lazy" />{:else}<span class="thumb-letter">{(row.name?.trim()[0] ?? '♪').toUpperCase()}</span>{/if}<span class="card-text"><span class="card-name">{row.name}</span><span class="meta">{row.role && kind === 'performers' && !role ? row.role + ' · ' : ''}<b>{count}</b>{countRest}</span></span></button>
    {:else}
    <button class="person graphic-{graphic}" style:--share={share.toFixed(3)} data-select-key={selectKey(row)} onclick={() => open(row)}>
      <span class="name">{row.name}</span>
      {#if graphic === 'dots'}<span class="dots">{#each [1, 2, 3, 4, 5] as dot}<i class:lit={dot <= Math.max(1, Math.round(share * 5))}></i>{/each}</span>{/if}
      <span class="meta">{row.role && kind === 'performers' && !role ? row.role + ' · ' : ''}{countText(row)}</span>
    </button>
    {/if}
  {/each}
</div>

<style>
  .chips { display: flex; gap: 6px; flex-wrap: wrap; margin-bottom: 12px; }
  /* Cards: taller glass rows with one cover on the left */
  .people.cards { grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 10px 14px; }
  .card-row { display: flex; align-items: center; gap: 12px; text-align: left; background: var(--panel); border: 1px solid var(--line); border-radius: 12px; color: var(--fg); padding: 8px; cursor: pointer; font: inherit; min-width: 0; backdrop-filter: blur(8px); }
  .card-row:hover { background: var(--panel-strong); }
  .card-row img, .thumb-letter { width: 56px; height: 56px; border-radius: 8px; object-fit: cover; flex: none; box-shadow: 0 3px 10px var(--shadow); }
  .thumb-letter { display: grid; place-items: center; background: color-mix(in srgb, var(--fg) 8%, transparent); color: var(--fg-quiet); font-size: 22px; font-weight: 600; }
  .card-text { display: flex; flex-direction: column; gap: 3px; min-width: 0; }
  .card-name { font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .card-row .meta b { color: var(--fg); }
  .people.tiles { grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 18px 16px; }
  .tile { display: flex; flex-direction: column; gap: 3px; text-align: left; background: transparent; border: 0; color: var(--fg); padding: 6px; border-radius: 12px; cursor: pointer; font: inherit; min-width: 0; }
  .tile:hover { background: var(--hover); }
  .tile-name { margin-top: 5px; font-weight: 600; font-size: 13px; overflow-wrap: anywhere; }
  .tile .meta b { color: var(--fg); }
  .chips button { border: 1px solid var(--line); background: transparent; color: var(--fg); border-radius: 14px; padding: 4px 12px; cursor: pointer; font-size: 12px; }
  .chips button.on { background: var(--accent); color: var(--on-accent); border-color: var(--accent); }
  .people { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 2px 16px; }
  .person { display: flex; justify-content: space-between; gap: 8px; text-align: left; background: transparent; border: 0; color: var(--fg); padding: 7px 10px; border-radius: 8px; cursor: pointer; font: inherit; }
  .person:hover { background: var(--hover); }
  .name { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .meta { color: var(--fg-quiet); font-size: 12px; white-space: nowrap; }
  .person { position: relative; align-items: center; }
  .dots { display: flex; gap: 3px; margin-left: auto; } .dots i { width: 5px; height: 5px; border-radius: 50%; background: color-mix(in srgb, var(--fg) 14%, transparent); } .dots i.lit { background: var(--accent); box-shadow: 0 0 5px color-mix(in srgb, var(--accent) 60%, transparent); }
  .graphic-bar::after { content: ""; position: absolute; left: 10px; bottom: 3px; height: 2px; width: calc(var(--share) * (100% - 20px)); border-radius: 2px; background: linear-gradient(90deg, var(--accent), color-mix(in srgb, var(--accent) 40%, #b48cff)); opacity: calc(0.35 + var(--share) * 0.65); }
  .graphic-bold .name { font-weight: calc(300 + var(--share) * 400); opacity: calc(0.45 + var(--share) * 0.55); } .graphic-bold .meta { opacity: calc(0.5 + var(--share) * 0.5); }
  .quiet { color: var(--fg-quiet); }
</style>
