<script lang="ts">
  import { query } from '../bridge'
  import { app, ui, openView } from '../state.svelte'
  let { kind }: { kind: 'artists' | 'composers' | 'performers' | 'genres' } = $props()
  let rows = $state<any[]>([])
  let role = $state('')
  const roleNames: Record<string, string> = { '': 'All', conductor: 'Conductors', band: 'Bands & orchestras', soloist: 'Soloists' }
  $effect(() => { app.indexVersion; app.librarySettings.AlbumSource; query(kind, { role }).then(result => rows = Array.isArray(result) ? result : []) })
  const plain = (text: string) => (text ?? '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')
  // Matches from the start of any word: "jarr" finds Keith Jarrett
  const shown = $derived(ui.search.length >= 2 ? rows.filter(row => plain(row.name).split(/[\s,.&/-]+/).some(word => word.startsWith(plain(ui.search)))) : rows)
  const viewKind = { artists: 'artist', composers: 'composer', performers: 'performer', genres: 'genre' }[kind]
</script>

{#if kind === 'performers'}
  <div class="chips">{#each Object.keys(roleNames) as roleKey}<button class:on={role === roleKey} onclick={() => role = roleKey}>{roleNames[roleKey]}</button>{/each}</div>
{/if}
{#if !rows.length}<p class="quiet">{app.library.text || 'Nothing here yet. The library is still syncing, or the server has none.'}</p>{/if}
<div class="people">
  {#each shown as row (row.key + (row.role ?? ''))}
    <button class="person" onclick={() => openView({ kind: viewKind, key: String(row.key), title: row.name })}>
      <span class="name">{row.name}</span>
      <span class="meta">{row.role && kind === 'performers' && !role ? row.role + ' · ' : ''}{row.albums ?? row.tracks ?? ''}{row.albums != null ? (row.albums === 1 ? ' album' : ' albums') : row.tracks != null ? ' tracks' : ''}</span>
    </button>
  {/each}
</div>

<style>
  .chips { display: flex; gap: 6px; margin-bottom: 12px; flex-wrap: wrap; }
  .chips button { border: 1px solid var(--line); background: transparent; color: var(--fg); border-radius: 14px; padding: 4px 12px; cursor: pointer; font-size: 12px; }
  .chips button.on { background: var(--accent); color: var(--on-accent); border-color: var(--accent); }
  .people { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 2px 16px; }
  .person { display: flex; justify-content: space-between; gap: 8px; text-align: left; background: transparent; border: 0; color: var(--fg); padding: 7px 10px; border-radius: 8px; cursor: pointer; font: inherit; }
  .person:hover { background: var(--hover); }
  .name { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .meta { color: var(--fg-quiet); font-size: 12px; white-space: nowrap; }
  .quiet { color: var(--fg-quiet); }
</style>
