<script lang="ts">
  import PinButton from '../parts/PinButton.svelte'
  import PageHead from '../parts/PageHead.svelte'
  import { selectable } from '../lib/selectable.svelte'
  import { browseActions } from '../lib/browseActions'
  import { query, artUrl } from '../bridge'
  import { app, ui, openView } from '../state.svelte'
  import AlbumView from './AlbumView.svelte'
  let { folderKey = '' }: { folderKey?: string } = $props()
  let folder = $state<any>({ children: [] })
  $effect(() => { app.indexVersion; query('folder', { key: folderKey }).then(result => folder = result ?? { children: [] }) })
  const plain = (text: string) => (text ?? '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '')
  const shown = $derived(ui.search.length >= 2 ? (folder.children ?? []).filter((child: any) => plain(child.name).includes(plain(ui.search))) : folder.children ?? [])
</script>

{#if folder.albumKey && !(folder.children ?? []).some((child: any) => child.subfolders > 0 || !child.albumKey)}
  <AlbumView albumKey={folder.albumKey} />
{:else}
  <PageHead title={folder.name ?? 'Folders'} detail={(folder.children ?? []).length ? `${(folder.children ?? []).length} items` : ''}>{#snippet titleExtra()}{#if folderKey}<PinButton view={{ kind: 'folder', key: folderKey, title: folder.name ?? 'Folder' }} />{/if}{/snippet}</PageHead>
  <div class="folders" use:selectable={{ scope: 'folder:' + folderKey, noun: 'folder', actions: browseActions('folder') }}>
    {#each shown as child (child.key)}
      <button class="folder" data-select-key={String(child.key)} onclick={() => openView({ kind: 'folder', key: String(child.key), title: child.name })}>
        {#if child.cover}<img src={artUrl(child.cover, 80)} alt="" loading="lazy" />{:else}<span class="icon">{child.subfolders ? '📁' : '♪'}</span>{/if}
        <span class="text"><span class="name">{child.name}</span><span class="meta">{child.subfolders ? `${child.subfolders} folders` : ''}{child.subfolders && child.tracks ? ' · ' : ''}{child.tracks ? `${child.tracks} tracks` : ''}{child.artist ? ` · ${child.artist}` : ''}</span></span>
      </button>
    {/each}
  </div>
{/if}

<style>
  .folders { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 4px 16px; }
  .folder { display: flex; align-items: center; gap: 10px; background: none; border: 0; color: var(--fg); padding: 6px 8px; border-radius: 8px; cursor: pointer; text-align: left; font: inherit; min-width: 0; }
  .folder:hover { background: var(--hover); }
  .folder img, .icon { width: 40px; height: 40px; border-radius: 6px; object-fit: cover; flex-shrink: 0; display: grid; place-items: center; background: var(--panel-strong); }
  .text { display: flex; flex-direction: column; min-width: 0; }
  .name { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .meta { color: var(--fg-quiet); font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
</style>
