<script lang="ts">
  import { query, artUrl, noArtUrl, sendToShell } from '../bridge'
  import { app, openView, toast, readSetting, writeSetting } from '../state.svelte'
  import { damaged } from '../lib/damaged.svelte'
  import PageHead from '../parts/PageHead.svelte'
  // Library health (SPEC.md › Library health): problems found in the library index, each opening its album or person; the shell keeps the report until the index changes
  let report = $state<any>(null), checking = $state(false), damagedFiles = $state<any[]>([])
  let classicalOnly = $state(readSetting('health.classicalOnly', '1') === '1'), tagKind = $state('all'), showAll = $state<Record<string, boolean>>({})
  async function load(rescan: boolean) { checking = true; report = await query('health', { rescan }); checking = false }
  $effect(() => { app.indexVersion; load(false) })
  $effect(() => { damaged.ids; query('damaged').then(rows => damagedFiles = Array.isArray(rows) ? rows : []) })
  const pageSize = 60
  const composerRows = $derived(((report?.missingComposer ?? []) as any[]).filter(row => !classicalOnly || row.classical))
  const tagKinds: [string, string][] = [['all', 'All'], ['artist', 'Album artist'], ['split', 'Split albums'], ['numbers', 'Track numbers'], ['year', 'Year']]
  const tagRows = $derived(((report?.tags ?? []) as any[]).filter(row => tagKind === 'all' || row.problems.some((problem: any) => problem.kind === tagKind)))
  const tagCount = (kind: string) => ((report?.tags ?? []) as any[]).filter(row => kind === 'all' || row.problems.some((problem: any) => problem.kind === kind)).length
  const nameCount = $derived((report?.splitNames?.length ?? 0) + (report?.spelledTwice?.length ?? 0))
  const tiles = $derived([['art', 'Missing cover art', report?.missingArt?.length ?? 0], ['composer', 'Missing composer', composerRows.length], ['duplicates', 'Duplicate albums', report?.duplicates?.length ?? 0], ['tags', 'Tag problems', report?.tags?.length ?? 0], ['names', 'Name problems', nameCount], ['damaged', 'Damaged files', damagedFiles.length]] as [string, string, number][])
  const checkedAt = $derived(report?.builtAt ? new Date(report.builtAt).toLocaleString(undefined, { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }) : '')
  const shown = (section: string, rows: any[]) => showAll[section] ? rows : rows.slice(0, pageSize)
  const openAlbum = (row: any) => openView({ kind: 'album', key: row.key, title: row.title })
  const openPerson = (role: string, key: string, name: string) => openView({ kind: role === 'conductor' ? 'performer' : role, key, title: name })
  async function openTrack(file: any) { const place = await query('trackPlace', { id: file.id }); if (place?.album) openView({ kind: 'album', key: String(place.album), title: place.albumTitle, highlight: String(file.id) }); else toast('That track isn’t in the library any more') }
  const jump = (section: string) => document.getElementById('health-' + section)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  function copy(lines: string[], noun: string) { navigator.clipboard.writeText(lines.join('\n')); toast(`Copied ${lines.length} ${noun}`) }
  const albumLine = (row: any, detail = '') => [row.artist, row.title, row.folder, detail].filter(Boolean).join(' — ')
  const setClassicalOnly = (value: boolean) => { classicalOnly = value; writeSetting('health.classicalOnly', value ? '1' : '0') }
</script>

{#snippet albumRow(row: any, detail: string)}
  <button class="row" onclick={() => openAlbum(row)} title="Open the album">
    <img src={row.cover ? artUrl(row.cover, 120) : noArtUrl} alt="" loading="lazy" />
    <span class="text"><span class="title">{row.title}</span><span class="quiet">{row.artist}{row.folder ? ` · ${row.folder}` : ''}</span></span>
    {#if detail}<span class="detail">{detail}</span>{/if}
  </button>
{/snippet}
{#snippet more(section: string, total: number)}
  {#if total > pageSize && !showAll[section]}<button class="ghost more" onclick={() => showAll[section] = true}>Show all {total}</button>{/if}
{/snippet}

<PageHead title="Library health" detail={report?.albums ? `${report.albums} ${report.folders ? 'album folders' : 'albums'} · ${report.tracks} tracks · checked ${checkedAt}` : ''}>
  {#if checking}<span class="quiet">{app.library.text || 'Checking…'}</span>{/if}
  <button class="ghost" disabled={checking} onclick={() => load(true)} title="Ask the server for changes, then check everything again">Rescan</button>
</PageHead>

{#if report?.empty}<p class="quiet">The library hasn’t been read from the server yet.</p>
{:else if !report}<p class="quiet">Checking the library…</p>
{:else}
  <div class="tiles">
    {#each tiles as [section, label, count] (section)}<button class="tile" class:clean={count === 0} onclick={() => jump(section)}><span class="count">{count === 0 ? '✓' : count}</span><span class="label">{label}</span></button>{/each}
  </div>

  <section class="card" id="health-art">
    <header><h3>Missing cover art</h3><span class="quiet">{report.missingArt.length}</span>{#if report.missingArt.length}<button class="ghost" onclick={() => copy(report.missingArt.map((row: any) => albumLine(row)), 'albums')}>Copy list</button>{/if}</header>
    <p class="callout">No cover in the folder or the files, or the server only has its placeholder. Add a folder.jpg or embed the art, let the server rescan, then press Rescan here.</p>
    {#each shown('art', report.missingArt) as row (row.key)}{@render albumRow(row, '')}{:else}<p class="quiet">Every album has cover art.</p>{/each}
    {@render more('art', report.missingArt.length)}
  </section>

  <section class="card" id="health-composer">
    <header><h3>Missing composer</h3><span class="quiet">{composerRows.length}</span>{#if composerRows.length}<button class="ghost" onclick={() => copy(composerRows.map((row: any) => albumLine(row, row.detail)), 'albums')}>Copy list</button>{/if}</header>
    <p class="callout">Tracks with no composer tag don’t appear under Composers or in composer views.</p>
    <div class="chips"><button class:on={classicalOnly} onclick={() => setClassicalOnly(true)}>Classical albums</button><button class:on={!classicalOnly} onclick={() => setClassicalOnly(false)}>All albums ({report.missingComposer.length})</button></div>
    {#if classicalOnly}<p class="quiet small">Classical means the album credits a conductor or orchestra, or has a classical genre (Classical, Baroque, Opera, Choral, Chamber and so on).</p>{/if}
    {#each shown('composer', composerRows) as row (row.key)}{@render albumRow(row, row.detail)}{:else}<p class="quiet">Every {classicalOnly ? 'classical ' : ''}track has a composer.</p>{/each}
    {@render more('composer', composerRows.length)}
  </section>

  <section class="card" id="health-duplicates">
    <header><h3>Duplicate albums</h3><span class="quiet">{report.duplicates.length}</span>{#if report.duplicates.length}<button class="ghost" onclick={() => copy(report.duplicates.flatMap((group: any) => [group.reason].concat(group.albums.map((row: any) => '  ' + albumLine(row, `${row.tracks} tracks, ${row.format}`)))), 'lines')}>Copy list</button>{/if}</header>
    <p class="callout">The same album in more than one place: folders with identical track lists, or the same artist and title with at least half the tracks in common. Different editions or remasters show here too; keep the ones you want.</p>
    {#each shown('duplicates', report.duplicates) as group, groupIndex (groupIndex)}
      <div class="group"><div class="group-head quiet">{group.reason}</div>{#each group.albums as row (row.key)}{@render albumRow(row, `${row.tracks} tracks · ${row.format}`)}{/each}</div>
    {:else}<p class="quiet">No duplicates found.</p>{/each}
    {@render more('duplicates', report.duplicates.length)}
  </section>

  <section class="card" id="health-tags">
    <header><h3>Tag problems</h3><span class="quiet">{report.tags.length}</span>{#if tagRows.length}<button class="ghost" onclick={() => copy(tagRows.map((row: any) => albumLine(row, row.problems.map((problem: any) => problem.text).join('; '))), 'albums')}>Copy list</button>{/if}</header>
    <p class="callout">Missing or inconsistent album artists, folders the server reads as more than one album (the album, album artist or year tag differs between tracks), missing, repeated or skipped track numbers, and missing, odd or mixed years.</p>
    <div class="chips">{#each tagKinds as [kind, label] (kind)}<button class:on={tagKind === kind} onclick={() => tagKind = kind}>{label} ({tagCount(kind)})</button>{/each}</div>
    {#each shown('tags', tagRows) as row (row.key)}
      <button class="row" onclick={() => openAlbum(row)} title="Open the album">
        <img src={row.cover ? artUrl(row.cover, 120) : noArtUrl} alt="" loading="lazy" />
        <span class="text"><span class="title">{row.title}</span><span class="quiet">{row.artist}{row.folder ? ` · ${row.folder}` : ''}</span></span>
        <span class="problems">{#each row.problems.filter((problem: any) => tagKind === 'all' || problem.kind === tagKind) as problem}<span class="problem">{problem.text}</span>{/each}</span>
      </button>
    {:else}<p class="quiet">No tag problems found.</p>{/each}
    {@render more('tags', tagRows.length)}
  </section>

  <section class="card" id="health-names">
    <header><h3>Name problems</h3><span class="quiet">{nameCount}</span></header>
    <p class="callout">One name the server has split in two, such as “Earth” and “Wind &amp; Fire” (two names that only ever appear together, one of them a single word), and one person spelled two ways, such as “Bach, Johann Sebastian” and “Johann Sebastian Bach”.</p>
    {#if report.splitNames.length}<h4>Possibly split in two</h4>{/if}
    {#each shown('split', report.splitNames) as row (row.names.join('|') + row.role)}
      <button class="row" onclick={() => row.key && openAlbum(row)} title="Open an album with these names">
        <span class="text"><span class="title">“{row.names[0]}” + “{row.names[1]}”</span><span class="quiet">{row.role} · {row.tracks} {row.tracks === 1 ? 'track' : 'tracks'}{row.title ? ` · on ${row.title}` : ''}</span></span>
        {#if row.sure}<span class="detail">Together they make a name you already have</span>{/if}
      </button>
    {/each}
    {@render more('split', report.splitNames.length)}
    {#if report.spelledTwice.length}<h4>Spelled more than one way</h4>{/if}
    {#each report.spelledTwice as group, groupIndex (groupIndex)}
      <div class="spelled"><span class="quiet role">{group.role}</span>{#each group.people as person (person.key)}<button class="link" onclick={() => openPerson(group.role, person.key, person.name)}>{person.name} <span class="quiet">({person.albums})</span></button>{/each}</div>
    {/each}
    {#if !nameCount}<p class="quiet">No name problems found.</p>{/if}
  </section>

  <section class="card" id="health-damaged">
    <header><h3>Damaged files</h3><span class="quiet">{damagedFiles.length}</span>{#if damagedFiles.length}<button class="ghost" onclick={() => copy(damagedFiles.map(file => `${file.unplayable ? 'unplayable' : file.errors + ' errors'}  ${file.path}${file.firstError ? '  (' + file.firstError + ')' : ''}`), 'files')}>Copy list</button><button class="ghost" onclick={() => sendToShell({ type: 'damagedClear' })}>Clear list</button>{/if}</header>
    <p class="callout warn">Tracks with frames that failed to decode while playing; there’s no up-front scan, so this grows as you listen. They play past the bad parts, which may skip or go quiet. Restore them from a backup or re-rip them, then clear the list.</p>
    {#each damagedFiles as file (file.path)}
      <button class="row" onclick={() => openTrack(file)} title="Open the album at this track">
        <span class="text"><span class="title">{file.title}</span><span class="quiet">{file.artist}{file.album ? ` · ${file.album}` : ''} · {file.path}</span></span>
        <span class="detail" title={file.firstError}>{file.unplayable ? 'Unplayable' : `${file.errors} ${file.errors === 1 ? 'error' : 'errors'}`}</span>
      </button>
    {:else}<p class="quiet">None found yet.</p>{/each}
  </section>
{/if}

<style>
  .tiles { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 10px; margin-bottom: 16px; }
  .tile { display: flex; flex-direction: column; align-items: flex-start; gap: 2px; background: var(--panel); border: 1px solid var(--line); border-radius: 12px; padding: 10px 14px; color: var(--fg); font: inherit; cursor: pointer; text-align: left; backdrop-filter: blur(8px); }
  .tile:hover { background: var(--panel-strong); }
  .tile .count { font-size: 24px; font-weight: 600; color: var(--warn); }
  .tile.clean .count { color: var(--accent); }
  .tile .label { font-size: 13px; color: var(--fg-quiet); }
  .card { background: var(--panel); border: 1px solid var(--line); border-radius: 12px; padding: 12px 16px 14px; margin-bottom: 14px; backdrop-filter: blur(8px); scroll-margin-top: calc(var(--page-head, 48px) + 8px); }
  header { display: flex; align-items: baseline; gap: 10px; }
  header button:first-of-type { margin-left: auto; }
  h3 { margin: 0; font-size: 17px; }
  h4 { margin: 12px 0 4px; font-size: 13px; color: var(--fg-quiet); font-weight: 600; }
  .callout { background: var(--accent-soft); border-radius: 8px; padding: 8px 12px; margin: 8px 0 10px; font-size: 13px; line-height: 1.45; }
  .callout.warn { background: color-mix(in srgb, var(--warn) 14%, transparent); }
  .small { font-size: 12px; margin: -4px 0 8px; }
  .row { display: flex; align-items: center; gap: 12px; width: 100%; background: none; border: 0; border-radius: 8px; color: var(--fg); font: inherit; text-align: left; padding: 5px 6px; cursor: pointer; }
  .row:hover { background: var(--hover); }
  .row img { width: 40px; height: 40px; border-radius: 6px; object-fit: cover; flex: none; }
  .text { flex: 1; min-width: 0; display: flex; flex-direction: column; }
  .text > span { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .text .quiet { font-size: 12px; }
  .detail { flex: none; max-width: 45%; font-size: 12px; color: var(--warn); text-align: right; }
  .problems { flex: none; max-width: 55%; display: flex; flex-direction: column; align-items: flex-end; gap: 2px; }
  .problem { font-size: 12px; color: var(--warn); text-align: right; }
  .group { border-top: 1px solid var(--line); padding-top: 6px; margin-top: 6px; }
  .group:first-of-type { border-top: 0; }
  .group-head { font-size: 12px; margin: 0 6px 2px; }
  .spelled { display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 14px; padding: 5px 6px; }
  .spelled .role { font-size: 12px; min-width: 70px; }
  .link { background: none; border: 0; padding: 0; color: var(--fg); font: inherit; cursor: pointer; text-decoration: underline; text-decoration-color: var(--line); }
  .link:hover { color: var(--accent); }
  .more { margin-top: 8px; }
</style>
