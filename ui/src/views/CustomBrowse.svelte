<script lang="ts">
  import { app, openView, openSection, type CustomStep } from '../state.svelte'
  import PeopleList from './PeopleList.svelte'
  import AlbumsOf from './AlbumsOf.svelte'
  import { levelNames } from '../lib/browse.svelte'
  import CustomViewCard from './CustomViewCard.svelte'
  // A custom view (SPEC.md › Custom views): each level lists the next step's rows within the view's genres; after the last step, the albums.
  // With group set, the sidebar entry shared by several views: a list of them
  let { viewId = '', group = '', filters = [] }: { viewId?: string; group?: string; filters?: CustomStep[] } = $props()
  const allViews = $derived((app.librarySettings.CustomViews ?? []) as any[])
  const definition = $derived(allViews.find(each => each.Id === viewId))
  const level = $derived(definition?.Levels?.[filters.length] ?? '')
  const heading = $derived(filters.length ? filters[filters.length - 1].name : definition?.Name ?? '')
  const trail = $derived([definition?.Name ?? ''].concat(filters.map(step => step.name)).join(' › '))
  const scope = $derived({ genres: definition?.Genres ?? [], filters: filters.map(step => ({ kind: step.kind, key: step.key })) })
</script>

{#if group}
  <div class="head"><h2>{group}</h2><button class="ghost" onclick={() => openSection('customViews')}>Edit views</button></div>
  <div class="cards">{#each allViews.filter(each => each.Enabled && each.Group === group) as each (each.Id)}<CustomViewCard view={each} />{/each}</div>
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
  .trail, .quiet { color: var(--fg-quiet); font-size: 12px; }
  .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 18px; }
</style>
