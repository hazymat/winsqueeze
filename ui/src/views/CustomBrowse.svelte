<script lang="ts">
  import { t, tn } from '../lib/i18n'
  import { app, openView, openSection, readSetting, writeSetting, type CustomStep } from '../state.svelte'
  import PeopleList from './PeopleList.svelte'
  import AlbumsOf from './AlbumsOf.svelte'
  import { levelNames, moveCustomView } from '../lib/browse.svelte'
  import { gridSortable } from '../lib/gridSortable'
  import CustomViewCard from './CustomViewCard.svelte'
  import PageHead from '../parts/PageHead.svelte'
  import ViewModes from '../parts/ViewModes.svelte'
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
  <PageHead title={group} detail={tn(groupViews.length, '{count} view', '{count} views') + ' · ' + t('hold and drag to reorder')}><ViewModes mode={groupLayout} onchange={setGroupLayout} /><button class="ghost" onclick={() => openSection('customViews')}>{t('Edit views')}</button></PageHead>
  <div class="cards layout-{groupLayout}" use:gridSortable={{ onMove: (from, to) => moveCustomView(groupViews.map(each => each.Id), from, to) }}>{#each groupViews as each (each.Id)}<div data-sort-row><CustomViewCard view={each} layout={groupLayout} /></div>{/each}</div>
{:else if !definition}
  <p class="quiet">{t('This custom view has been deleted.')} <button class="ghost" onclick={() => openSection('customViews')}>{t('Custom views')}</button></p>
{:else if level}
  <PeopleList kind="custom" title={heading} detail={(filters.length ? trail + ' › ' : '') + (levelNames[level] ?? level)} custom={{ viewId, genres: scope.genres, filters, level }} />
{:else}
  <AlbumsOf kind="custom" key={viewId} title={heading} params={scope} />
{/if}

<style>
  .quiet { color: var(--fg-quiet); font-size: 12px; }
  .cards.layout-list { grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 8px 14px; }
  .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 18px; }
  .cards > :global([data-sort-row]) { transition: transform 0.15s, box-shadow 0.15s; border-radius: 14px; }
  .cards > :global(.lifted) { position: relative; z-index: 20; transition: none; box-shadow: 0 18px 40px var(--shadow); cursor: grabbing; }
  .cards > :global(.drop-target) { outline: 2px dashed var(--accent); outline-offset: 4px; }
  .cards.layout-tiles { grid-template-columns: repeat(auto-fill, minmax(170px, 1fr)); gap: 14px; }
</style>
