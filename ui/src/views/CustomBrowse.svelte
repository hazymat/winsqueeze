<script lang="ts">
  import { app, openView, openSection, readSetting, writeSetting, type CustomStep } from '../state.svelte'
  import PeopleList from './PeopleList.svelte'
  import AlbumsOf from './AlbumsOf.svelte'
  import { levelNames, moveCustomView } from '../lib/browse.svelte'
  import { gridSortable } from '../lib/gridSortable'
  import CustomViewCard from './CustomViewCard.svelte'
  // A custom view (SPEC.md › Custom views): each level lists the next step's rows within the view's genres; after the last step, the albums.
  // With group set, the sidebar entry shared by several views: a list of them
  let { viewId = '', group = '', filters = [] }: { viewId?: string; group?: string; filters?: CustomStep[] } = $props()
  const allViews = $derived((app.librarySettings.CustomViews ?? []) as any[])
  const definition = $derived(allViews.find(each => each.Id === viewId))
  const level = $derived(definition?.Levels?.[filters.length] ?? '')
  const heading = $derived(filters.length ? filters[filters.length - 1].name : definition?.Name ?? '')
  const trail = $derived([definition?.Name ?? ''].concat(filters.map(step => step.name)).join(' › '))
  // A group's page shows its views as Cards or smaller square Tiles, remembered per PC
  let groupLayout = $state(readSetting('customGroupLayout', 'cards'))
  const setGroupLayout = (value: string) => { groupLayout = value; writeSetting('customGroupLayout', value) }
  const groupViews = $derived(allViews.filter(each => each.Enabled && each.Group === group))
  const scope = $derived({ genres: definition?.Genres ?? [], filters: filters.map(step => ({ kind: step.kind, key: step.key })) })
</script>

{#if group}
  <div class="head"><h2>{group}</h2><span class="grow"></span><div class="layout-toggle">{#each [['cards', 'Cards'], ['tiles', 'Tiles']] as [value, label]}<button class:on={groupLayout === value} onclick={() => setGroupLayout(value)}>{label}</button>{/each}</div><button class="ghost" onclick={() => openSection('customViews')}>Edit views</button></div>
  <div class="cards" class:compact={groupLayout === 'tiles'} use:gridSortable={{ onMove: (from, to) => moveCustomView(groupViews.map(each => each.Id), from, to) }}>{#each groupViews as each (each.Id)}<div data-sort-row><CustomViewCard view={each} compact={groupLayout === 'tiles'} /></div>{/each}</div>
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
  .cards > :global([data-sort-row]) { transition: transform 0.15s, box-shadow 0.15s; border-radius: 14px; }
  .cards > :global(.lifted) { position: relative; z-index: 20; transition: none; box-shadow: 0 18px 40px var(--shadow); cursor: grabbing; }
  .cards > :global(.drop-target) { outline: 2px dashed var(--accent); outline-offset: 4px; }
  .cards.compact { grid-template-columns: repeat(auto-fill, minmax(170px, 1fr)); gap: 14px; }
  .grow { flex: 1; }
  .layout-toggle { display: flex; border: 1px solid var(--line); border-radius: 999px; overflow: hidden; align-self: center; }
  .layout-toggle button { border: 0; background: transparent; color: var(--fg-quiet); padding: 4px 12px; font: inherit; font-size: 12px; cursor: pointer; }
  .layout-toggle button.on { background: var(--accent-soft); color: var(--accent); }
</style>
