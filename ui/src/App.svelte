<script lang="ts">
  import { sendToShell, onShellMessage, request, command } from './bridge'
  import { app, nav, ui, playlist, openSection, back, currentView, newPlaylist, undoLastAdd, writeSetting } from './state.svelte'
  import TransportBar from './parts/TransportBar.svelte'
  import NowPlaying from './parts/NowPlaying.svelte'
  import PlaylistPanel from './parts/PlaylistPanel.svelte'
  import PeopleList from './views/PeopleList.svelte'
  import AlbumsOf from './views/AlbumsOf.svelte'
  import AlbumView from './views/AlbumView.svelte'
  import FolderView from './views/FolderView.svelte'
  import SearchView from './views/SearchView.svelte'
  import FavouritesView from './views/FavouritesView.svelte'
  import QueueView from './views/QueueView.svelte'

  let searchBox = $state<HTMLInputElement>()
  let width = $state(window.innerWidth)
  let height = $state(window.innerHeight)
  const mini = $derived(width < 420 && height < 260)
  const vertical = $derived(!mini && width <= 900)
  const folderMode = $derived(app.librarySettings.AlbumSource !== 'tags')
  const sections = $derived([['favourites', 'Favourites', '★'], ['artists', 'Artists', '👤'], ['genres', 'Genres', '🏷'], ['composers', 'Composers', '🎼'], ['performers', 'Performers', '🎻'], ['folders', folderMode ? 'Folders' : 'Albums', folderMode ? '📁' : '💿'], ['queue', 'Queue', '☰']])
  const view = $derived(currentView())
  const everything = $derived(ui.search.length >= 2 && (ui.scope === 'everything' || ['queue'].includes(view.kind)))

  onShellMessage('status', message => app.status = message)
  onShellMessage('nowPlaying', message => { app.nowPlaying = message.status; app.receivedAt = Date.now() })
  onShellMessage('players', message => { app.players = message.players ?? []; app.ownPlayer = message.own ?? ''; if (!app.selectedPlayer) app.selectedPlayer = message.selected ?? '' })
  onShellMessage('library', message => app.library = { text: message.text ?? '', done: message.done ?? 0, total: message.total ?? 0 })
  onShellMessage('indexUpdated', () => app.indexVersion++)
  onShellMessage('settings', message => { if (message.library) app.librarySettings = message.library })
  sendToShell({ type: 'ready' })
  // Restore an unsaved playlist draft from last time
  request({ type: 'loadDraft' }).then(draft => { if (draft?.items) newPlaylist(draft.name ?? 'Untitled playlist', draft.items) })

  // Skins (SPEC.md › Skins): the skin is a data attribute; tokens.css holds the variables
  $effect(() => { document.documentElement.dataset.skin = ui.skin })

  function onKey(event: KeyboardEvent) {
    const typing = (event.target as HTMLElement)?.matches?.('input, textarea, select')
    if (event.key === ' ' && !typing) { event.preventDefault(); command('pause', app.nowPlaying?.mode === 'play' ? '1' : '0') }
    else if (event.ctrlKey && event.key === 'ArrowRight') { event.preventDefault(); command('playlist', 'index', '+1') }
    else if (event.ctrlKey && event.key === 'ArrowLeft') { event.preventDefault(); command('playlist', 'index', '-1') }
    else if (event.ctrlKey && event.key === 'ArrowUp') { event.preventDefault(); command('mixer', 'volume', '+5') }
    else if (event.ctrlKey && event.key === 'ArrowDown') { event.preventDefault(); command('mixer', 'volume', '-5') }
    else if (event.ctrlKey && event.key.toLowerCase() === 'f') { event.preventDefault(); searchBox?.focus(); searchBox?.select() }
    else if (event.ctrlKey && event.key.toLowerCase() === 'n') { event.preventDefault(); if (!playlist.open) newPlaylist(); else playlist.collapsed = false }
    else if (event.ctrlKey && event.key.toLowerCase() === 'z' && !typing && playlist.history.length) { event.preventDefault(); undoLastAdd() }
    else if (event.key === 'Escape') { if (ui.expanded) ui.expanded = false; else if (ui.search) ui.search = ''; else if (nav.stack.length) back() }
    else if (event.altKey && event.key === 'ArrowLeft' && nav.stack.length) { event.preventDefault(); back() }
  }
</script>

<svelte:window bind:innerWidth={width} bind:innerHeight={height} onkeydown={onKey} onmouseup={(event) => { if (event.button === 3 && nav.stack.length) back() }} />

{#if mini}
  <div class="mini-shell"><TransportBar mini={true} /></div>
{:else}
  <div class="shell" class:vertical>
    {#if !vertical}
      <nav class="sidebar">
        <div class="brand">WinSqueeze</div>
        {#each sections as [key, label, glyph] (key)}
          <button class="section" class:on={nav.section === key && !everything} onclick={() => openSection(key)}><span class="glyph">{glyph}</span>{label}</button>
        {/each}
        <button class="section new" onclick={() => playlist.open ? (playlist.collapsed = false) : newPlaylist()}><span class="glyph">＋</span>New playlist</button>
        <div class="spacer"></div>
        {#if app.library.total && app.library.done < app.library.total}<div class="sync" title={app.library.text}><div class="bar" style:width={`${(app.library.done / app.library.total) * 100}%`}></div></div>{/if}
        {#if app.library.text}<div class="sync-text">{app.library.text}</div>{/if}
        <select class="skin" bind:value={ui.skin} onchange={() => writeSetting('skin', ui.skin)} aria-label="Skin"><option value="auto">Skin: follow Windows</option><option value="dark">Skin: frosted dark</option><option value="light">Skin: frosted light</option><option value="classic">Skin: Squeezebox classic</option><option value="amber">Skin: hi-fi amber</option></select>
        <button class="section" onclick={() => sendToShell({ type: 'openSettings' })}><span class="glyph">⚙</span>Settings</button>
      </nav>
    {/if}
    <div class="content-column">
      {#if app.status?.updateReady}
        <div class="update-notice"><span>WinSqueeze {app.status.updateReady} is ready.</span><button class="primary" onclick={() => sendToShell({ type: 'applyUpdate' })}>Restart to update</button></div>
      {/if}
      <header class="topbar">
        {#if nav.stack.length}<button class="ghost back" onclick={back} aria-label="Back">‹</button>{/if}
        <div class="search">
          <input bind:this={searchBox} bind:value={ui.search} placeholder={ui.scope === 'everything' ? 'Search everything' : 'Search this list'} aria-label="Search" />
          <div class="scope">
            <button class:on={ui.scope === 'list'} onclick={() => ui.scope = 'list'}>This list</button>
            <button class:on={ui.scope === 'everything'} onclick={() => ui.scope = 'everything'}>Everything</button>
          </div>
        </div>
        {#if vertical}<button class="ghost" onclick={() => sendToShell({ type: 'openSettings' })} aria-label="Settings">⚙</button>{/if}
      </header>
      <div class="body">
        <main class="content">
          {#if everything}
            <SearchView />
          {:else if view.kind === 'artists' || view.kind === 'composers' || view.kind === 'performers' || view.kind === 'genres'}
            {#key view.kind}<PeopleList kind={view.kind} />{/key}
          {:else if view.kind === 'folders'}
            {#if folderMode}<FolderView />{:else}<AlbumsOf kind="albums" title="Albums" />{/if}
          {:else if view.kind === 'folder'}
            {#key view.key}<FolderView folderKey={view.key} />{/key}
          {:else if view.kind === 'album'}
            {#key view.key}<AlbumView albumKey={view.key ?? ''} />{/key}
          {:else if view.kind === 'artist' || view.kind === 'composer' || view.kind === 'performer' || view.kind === 'genre'}
            {#key view.kind + view.key}<AlbumsOf kind={view.kind} key={view.key} title={view.title} />{/key}
          {:else if view.kind === 'favourites'}
            {#key view.key}<FavouritesView itemId={view.key ?? ''} />{/key}
          {:else if view.kind === 'queue'}
            <QueueView />
          {/if}
        </main>
        <PlaylistPanel />
        {#if ui.expanded}<NowPlaying />{/if}
      </div>
      <TransportBar />
      {#if vertical}
        <nav class="tabbar">
          {#each sections as [key, label, glyph] (key)}
            <button class:on={nav.section === key} onclick={() => openSection(key)}><span class="glyph">{glyph}</span><span class="tab-label">{label}</span></button>
          {/each}
        </nav>
      {/if}
    </div>
  </div>
{/if}

{#if ui.toast}
  <div class="toast" role="status">{ui.toast.text}{#if ui.toast.undo}<button onclick={() => { ui.toast?.undo?.(); ui.toast = null }}>Undo</button>{/if}</div>
{/if}

<style>
  .shell { display: grid; grid-template-columns: 210px minmax(0, 1fr); height: 100vh; }
  .shell.vertical { grid-template-columns: minmax(0, 1fr); }
  .sidebar { display: flex; flex-direction: column; gap: 2px; padding: 14px 10px; background: var(--glass); backdrop-filter: blur(var(--blur)) saturate(1.4); border-right: 1px solid var(--line); min-height: 0; }
  .brand { font-family: var(--font-display); color: var(--readout); text-shadow: 0 0 8px var(--readout-glow); letter-spacing: 0.08em; padding: 4px 10px 14px; }
  .section { display: flex; align-items: center; gap: 10px; background: none; border: 0; color: var(--fg); padding: 8px 10px; border-radius: 8px; cursor: pointer; font: inherit; text-align: left; }
  .section:hover { background: var(--hover); }
  .section.on { background: var(--accent-soft); color: var(--accent); }
  .section.new { color: var(--accent); margin-top: 8px; }
  .glyph { width: 20px; text-align: center; }
  .spacer { flex: 1; }
  .sync { height: 3px; background: var(--track); border-radius: 2px; margin: 0 10px; overflow: hidden; }
  .bar { height: 100%; background: var(--accent); }
  .skin { margin: 4px 10px; background: var(--panel); color: var(--fg-quiet); border: 1px solid var(--line); border-radius: 6px; padding: 3px 4px; font: inherit; font-size: 12px; }
  .sync-text { color: var(--fg-quiet); font-size: 11px; padding: 4px 10px 8px; }
  .content-column { display: flex; flex-direction: column; min-width: 0; min-height: 0; height: 100vh; }
  .update-notice { display: flex; align-items: center; justify-content: center; gap: 12px; padding: 6px 12px; background: var(--accent-soft); font-size: 13px; }
  .topbar { display: flex; align-items: center; gap: 8px; padding: 10px 16px; }
  .back { font-size: 20px; padding: 2px 12px; }
  .search { flex: 1; display: flex; align-items: center; gap: 6px; background: var(--panel); border: 1px solid var(--line); border-radius: 10px; padding: 2px 4px 2px 12px; max-width: 640px; }
  .search input { flex: 1; min-width: 0; background: none; border: 0; outline: none; color: var(--fg); font: inherit; padding: 6px 0; }
  .scope { display: flex; gap: 2px; }
  .scope button { background: none; border: 0; color: var(--fg-quiet); font: inherit; font-size: 12px; border-radius: 7px; padding: 4px 8px; cursor: pointer; white-space: nowrap; }
  .scope button.on { background: var(--accent-soft); color: var(--accent); }
  .body { flex: 1; display: flex; min-height: 0; position: relative; }
  .content { flex: 1; overflow-y: auto; padding: 4px 20px 24px; min-width: 0; }
  .tabbar { display: flex; justify-content: space-around; background: var(--glass); backdrop-filter: blur(var(--blur)); border-top: 1px solid var(--line); }
  .tabbar button { flex: 1; display: flex; flex-direction: column; align-items: center; gap: 2px; background: none; border: 0; color: var(--fg-quiet); font: inherit; font-size: 10px; padding: 6px 0; cursor: pointer; min-width: 0; }
  .tabbar button.on { color: var(--accent); }
  .tab-label { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 100%; }
  @media (max-width: 480px) { .tab-label { display: none; } .tabbar .glyph { font-size: 18px; } }
  .mini-shell { height: 100vh; background: var(--glass); }
  .toast { position: fixed; left: 50%; bottom: 100px; transform: translateX(-50%); z-index: 50; display: flex; gap: 14px; align-items: center; background: var(--panel-solid); color: var(--fg); border: 1px solid var(--line); border-radius: 10px; padding: 10px 16px; box-shadow: 0 10px 30px var(--shadow); font-size: 13px; }
  .toast button { background: none; border: 0; color: var(--accent); font: inherit; font-weight: 600; cursor: pointer; }
</style>
