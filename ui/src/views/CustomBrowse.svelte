<script lang="ts">
  import { app, openView, openSection, type CustomStep } from '../state.svelte'
  import PeopleList from './PeopleList.svelte'
  import AlbumsOf from './AlbumsOf.svelte'
  import { levelNames } from '../lib/browse.svelte'
  // A custom view (SPEC.md › Custom views): each level lists the next step's rows within the view's genres; after the last step, the albums.
  // With group set, the sidebar entry shared by several views: a list of them
  let { viewId = '', group = '', filters = [] }: { viewId?: string; group?: string; filters?: CustomStep[] } = $props()
  const allViews = $derived((app.librarySettings.CustomViews ?? []) as any[])
  const definition = $derived(allViews.find(each => each.Id === viewId))
  const level = $derived(definition?.Levels?.[filters.length] ?? '')
  const stepsText = (each: any) => (each.Levels ?? []).map((name: string) => levelNames[name] ?? name).concat(['Albums']).join(' › ')
  const heading = $derived(filters.length ? filters[filters.length - 1].name : definition?.Name ?? '')
  const trail = $derived([definition?.Name ?? ''].concat(filters.map(step => step.name)).join(' › '))
  const scope = $derived({ genres: definition?.Genres ?? [], filters: filters.map(step => ({ kind: step.kind, key: step.key })) })
</script>

{#if group}
  <div class="head"><h2>{group}</h2><button class="ghost" onclick={() => openSection('customViews')}>Edit views</button></div>
  <div class="views">
    {#each allViews.filter(each => each.Enabled && each.Group === group) as each (each.Id)}
      <button class="view-row" onclick={() => openView({ kind: 'custom', key: each.Id, title: each.Name, filters: [] })}><span class="name">{each.Name}</span><span class="meta">{stepsText(each)}</span></button>
    {/each}
  </div>
{:else if !definition}
  <p class="quiet">This custom view has been deleted. <button class="ghost" onclick={() => openSection('customViews')}>Custom views</button></p>
{:else if level}
  <div class="head"><h2>{heading}</h2><span class="trail">{filters.length ? trail + ' › ' : ''}{levelNames[level] ?? level}</span></div>
  <PeopleList kind="custom" custom={{ viewId, genres: scope.genres, filters, level }} />
{:else}
  <AlbumsOf kind="custom" key={viewId} title={heading} params={scope} />
{/if}

<style>
  .head { display: flex; align-items: baseline; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; }
  h2 { margin: 0; font-size: 24px; }
  .trail, .meta, .quiet { color: var(--fg-quiet); font-size: 12px; }
  .views { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 2px 16px; }
  .view-row { display: flex; justify-content: space-between; gap: 8px; text-align: left; background: transparent; border: 0; color: var(--fg); padding: 7px 10px; border-radius: 8px; cursor: pointer; font: inherit; }
  .view-row:hover { background: var(--hover); }
  .name { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
</style>
