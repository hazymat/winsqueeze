<script lang="ts">
  import { app, toast, openView } from '../state.svelte'
  import { sendToShell, query } from '../bridge'
  import { levelNames, customViewsExplainer } from '../lib/browse.svelte'
  // Custom views editor (SPEC.md › Custom views): every change is saved straight away, in settings (so export/import carries them)
  type CustomView = { Id: string; Name: string; Enabled: boolean; Group: string; Genres: string[]; Levels: string[] }
  let views = $state<CustomView[]>(JSON.parse(JSON.stringify(app.librarySettings.CustomViews ?? [])))
  let libraryGenres = $state<string[]>([])
  $effect(() => { app.indexVersion; query('genres').then(rows => libraryGenres = Array.isArray(rows) ? rows.map((row: any) => String(row.name)) : []) })
  let pickingGenresFor = $state(''), genreFilter = $state('')
  function save(next: CustomView[]) { views = next; app.librarySettings.CustomViews = JSON.parse(JSON.stringify(next)); sendToShell({ type: 'setCustomViews', views: app.librarySettings.CustomViews }) }
  const change = (viewId: string, update: (view: CustomView) => void) => save(views.map(view => { if (view.Id !== viewId) return view; const copy: CustomView = JSON.parse(JSON.stringify(view)); update(copy); return copy }))
  const addView = () => save(views.concat([{ Id: 'view-' + Date.now(), Name: 'New view', Enabled: false, Group: '', Genres: [], Levels: ['genre', 'composer'] }]))
  const duplicateView = (view: CustomView) => { const copy: CustomView = JSON.parse(JSON.stringify(view)); copy.Id = 'view-' + Date.now(); copy.Name = view.Name + ' (copy)'; copy.Enabled = false; const next = views.slice(); next.splice(views.indexOf(view) + 1, 0, copy); save(next) }
  function deleteView(view: CustomView) { const before = views.slice(); save(views.filter(each => each.Id !== view.Id)); toast(`Deleted “${view.Name}”`, () => save(before), 8000, { redo: () => save(before.filter(each => each.Id !== view.Id)) }) }
  const toggleGenre = (view: CustomView, genre: string) => change(view.Id, copy => copy.Genres = copy.Genres.some(each => plain(each) === plain(genre)) ? copy.Genres.filter(each => plain(each) !== plain(genre)) : copy.Genres.concat([genre]).sort((first, second) => first.localeCompare(second)))
  const plain = (text: string) => text.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')
  const genreChoices = $derived(libraryGenres.filter(genre => !genreFilter || plain(genre).includes(plain(genreFilter))))
  const inLibrary = (genre: string) => !libraryGenres.length || libraryGenres.some(each => plain(each) === plain(genre))
  const groupNames = $derived(Array.from(new Set(views.map(view => view.Group).filter(Boolean))))
</script>

<div class="head"><h2>Custom views</h2><button class="ghost" onclick={addView}>＋ New view</button></div>
<p class="explainer">{customViewsExplainer}</p>
<p class="quiet">Switch a view on to add it to the sidebar. Views with the same group share one sidebar entry named after the group; leave the group empty to give a view its own entry. Drag them into place with Customise sidebar.</p>
<datalist id="custom-view-groups">{#each groupNames as groupName}<option value={groupName}></option>{/each}</datalist>
{#each views as view (view.Id)}
  <section class="view" class:off={!view.Enabled}>
    <div class="row">
      <label class="switch"><input type="checkbox" checked={view.Enabled} onchange={() => change(view.Id, copy => copy.Enabled = !copy.Enabled)} />Show</label>
      <input class="name" value={view.Name} aria-label="Name" onchange={(event) => change(view.Id, copy => copy.Name = (event.currentTarget as HTMLInputElement).value.trim() || 'Untitled view')} />
      <label class="group">Group <input list="custom-view-groups" value={view.Group} placeholder="none" onchange={(event) => change(view.Id, copy => copy.Group = (event.currentTarget as HTMLInputElement).value.trim())} /></label>
      <span class="grow"></span>
      <button class="ghost" onclick={() => openView({ kind: 'custom', key: view.Id, title: view.Name, filters: [] })}>Open</button>
      <button class="ghost" onclick={() => duplicateView(view)}>Duplicate</button>
      <button class="ghost" onclick={() => deleteView(view)}>Delete</button>
    </div>
    <div class="row steps">
      <span class="label">Steps</span>
      {#each view.Levels as level, position}
        <select value={level} onchange={(event) => change(view.Id, copy => copy.Levels[position] = (event.currentTarget as HTMLSelectElement).value)}>{#each Object.entries(levelNames) as [value, label]}<option {value}>{label.replace(/s$/, '')}</option>{/each}</select>
        <button class="remove" aria-label="Remove step" title="Remove step" onclick={() => change(view.Id, copy => copy.Levels.splice(position, 1))}>×</button>
        <span class="arrow">›</span>
      {/each}
      {#if view.Levels.length < 4}<button class="ghost small" onclick={() => change(view.Id, copy => copy.Levels.push(copy.Levels.includes('composer') ? 'genre' : 'composer'))}>＋ Step</button><span class="arrow">›</span>{/if}
      <span>Albums</span>
    </div>
    <div class="row genres">
      <span class="label">Genres</span>
      {#if !view.Genres.length}<span class="quiet">All genres (whole library)</span>{/if}
      {#each view.Genres as genre}<span class="chip" class:missing={!inLibrary(genre)} title={inLibrary(genre) ? '' : 'Not in your library'}>{genre}<button aria-label="Remove {genre}" onclick={() => toggleGenre(view, genre)}>×</button></span>{/each}
      <button class="ghost small" onclick={() => { pickingGenresFor = pickingGenresFor === view.Id ? '' : view.Id; genreFilter = '' }}>{pickingGenresFor === view.Id ? 'Done' : 'Choose genres…'}</button>
    </div>
    {#if pickingGenresFor === view.Id}
      <div class="picker">
        <input placeholder="Filter genres" bind:value={genreFilter} />
        <div class="genre-list">{#each genreChoices as genre}<label><input type="checkbox" checked={view.Genres.some(each => plain(each) === plain(genre))} onchange={() => toggleGenre(view, genre)} />{genre}</label>{/each}</div>
      </div>
    {/if}
  </section>
{/each}
{#if !views.length}<p class="quiet">No custom views. Make one with New view.</p>{/if}

<style>
  .head { display: flex; align-items: center; gap: 12px; margin-bottom: 8px; }
  h2 { margin: 0; font-size: 24px; flex: 1; }
  .explainer { max-width: 760px; line-height: 1.5; margin: 0 0 6px; }
  .quiet { color: var(--fg-quiet); font-size: 12px; max-width: 760px; }
  .view { border: 1px solid var(--line); border-radius: 10px; padding: 10px 12px; margin-top: 12px; background: var(--panel); }
  .view.off .name { opacity: 0.6; }
  .row { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
  .row + .row { margin-top: 8px; }
  .grow { flex: 1; }
  .label { color: var(--fg-quiet); font-size: 12px; width: 52px; }
  .name { font: inherit; font-weight: 600; min-width: 180px; }
  input, select { background: var(--bg, transparent); color: var(--fg); border: 1px solid var(--line); border-radius: 6px; padding: 4px 8px; font: inherit; font-size: 13px; }
  .switch, .group { display: inline-flex; align-items: center; gap: 6px; font-size: 13px; }
  .group input { width: 130px; }
  .arrow { color: var(--fg-quiet); }
  .remove, .chip button { background: none; border: 0; color: var(--fg-quiet); cursor: pointer; font: inherit; padding: 0 4px; }
  .remove:hover, .chip button:hover { color: var(--fg); }
  .small { padding: 3px 10px; font-size: 12px; }
  .chip { display: inline-flex; align-items: center; border: 1px solid var(--line); border-radius: 12px; padding: 2px 4px 2px 10px; font-size: 12px; }
  .chip.missing { opacity: 0.55; text-decoration: line-through; }
  .picker { margin-top: 8px; }
  .picker > input { width: 240px; }
  .genre-list { margin-top: 6px; max-height: 220px; overflow: auto; display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 2px 12px; }
  .genre-list label { display: flex; align-items: center; gap: 6px; font-size: 13px; padding: 2px 0; }
</style>
