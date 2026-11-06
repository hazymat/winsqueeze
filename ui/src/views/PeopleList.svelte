<script lang="ts">
  import { selectable } from '../lib/selectable.svelte'
  import { browseActions } from '../lib/browseActions'
  import { query } from '../bridge'
  import { app, ui, openView, readSetting, writeSetting, type CustomStep } from '../state.svelte'
  import CoverMosaic from '../parts/CoverMosaic.svelte'
  import { browseState } from '../lib/browse.svelte'
  // custom: one level of a custom view (SPEC.md › Custom views); rows come from its scope, and a click takes the next step
  let { kind, custom }: { kind: 'artists' | 'composers' | 'performers' | 'genres' | 'custom'; custom?: { viewId: string; genres: string[]; filters: CustomStep[]; level: string } } = $props()
  let rows = $state<any[]>([])
  let role = $state('')
  const roleNames: Record<string, string> = { '': 'All', conductor: 'Conductors', band: 'Bands & orchestras', soloist: 'Soloists' }
  $effect(() => { app.indexVersion; app.librarySettings.AlbumSource; (custom ? query('customLevel', { genres: custom.genres, filters: custom.filters, level: custom.level }) : query(kind, { role })).then(result => rows = Array.isArray(result) ? result : []) })
  const plain = (text: string) => (text ?? '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')
  // Matches from the start of any word: "jarr" finds Keith Jarrett
  const shown = $derived(ui.search.length >= 2 ? rows.filter(row => plain(row.name).split(/[\s,.&/-]+/).some(word => word.startsWith(plain(ui.search)))) : rows)
  const viewKind: string = custom ? custom.level : ({ artists: 'artist', composers: 'composer', performers: 'performer', genres: 'genre' } as Record<string, string>)[kind] // keyed per kind (or custom step) by the parent
  const stepTo = (row: any): CustomStep[] => custom!.filters.concat([{ kind: custom!.level, key: String(row.key), name: row.name }])
  // A custom row is selected (and played) as its whole scope: the view's genres plus every step to it
  const selectKey = (row: any) => custom ? JSON.stringify({ genres: custom.genres, filters: stepTo(row).map(step => ({ kind: step.kind, key: step.key })) }) : String(row.key)
  const open = (row: any) => custom ? openView({ kind: 'custom', key: custom.viewId, title: row.name, filters: stepTo(row) }) : openView({ kind: viewKind, key: String(row.key), title: row.name })
  // The count matches what the next view groups by (album artists, years…), or tracks, or both (Settings › Library)
  const nextView = custom ? { group: 'none' } : browseState(viewKind) // keyed per kind by the parent
  const groupCounts: Record<string, [string, string, string]> = { artist: ['artists', 'album artist', 'album artists'], year: ['years', 'year', 'years'], decade: ['decades', 'decade', 'decades'], format: ['formats', 'format', 'formats'] }
  const plural = (count: number, one: string, many: string) => `${count} ${count === 1 ? one : many}`
  function countText(row: any) {
    const [field, one, many] = groupCounts[nextView.group] ?? ['albums', 'album', 'albums']
    const grouped = row[field] != null ? plural(Number(row[field]), one, many) : '', tracks = row.tracks != null ? plural(Number(row.tracks), 'track', 'tracks') : ''
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

<div class="bar">
  {#if kind === 'performers'}<div class="chips">{#each Object.keys(roleNames) as roleKey}<button class:on={role === roleKey} onclick={() => role = roleKey}>{roleNames[roleKey]}</button>{/each}</div>{/if}
  <div class="layout-toggle">{#each [['list', 'List'], ['tiles', 'Tiles']] as [value, label]}<button class:on={layout === value} onclick={() => setLayout(value)}>{label}</button>{/each}</div>
</div>
{#if !rows.length}<p class="quiet">{app.library.text || (custom ? 'Nothing in your library matches this view. Check its genres in Custom views.' : 'Nothing here yet. The library is still syncing, or the server has none.')}</p>{/if}
<div class="people" class:tiles={layout === 'tiles'} use:selectable={{ scope: 'people:' + kind + (custom ? custom.viewId + custom.filters.length : ''), noun: viewKind ?? 'item', actions: browseActions(custom ? 'custom' : viewKind ?? 'artist') }}>
  {#each shown as row (row.key + (row.role ?? ''))}
    {@const share = graphic === 'none' ? 0 : shareOf(row)}
    {#if layout === 'tiles'}
      {@const [count, countRest] = countParts(row)}
      <button class="tile" data-select-key={selectKey(row)} onclick={() => open(row)} title={row.name}><CoverMosaic covers={row.covers ?? []} name={row.name} /><span class="tile-name">{row.name}</span><span class="meta"><b>{count}</b>{countRest}</span></button>
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
  .bar { display: flex; align-items: flex-start; gap: 12px; margin-bottom: 12px; }
  .chips { display: flex; gap: 6px; flex-wrap: wrap; flex: 1; }
  .layout-toggle { margin-left: auto; display: flex; border: 1px solid var(--line); border-radius: 999px; overflow: hidden; flex: none; }
  .layout-toggle button { border: 0; background: transparent; color: var(--fg-quiet); padding: 4px 12px; font: inherit; font-size: 12px; cursor: pointer; }
  .layout-toggle button.on { background: var(--accent-soft); color: var(--accent); }
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
