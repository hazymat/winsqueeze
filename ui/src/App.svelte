<script lang="ts">
  import { sendToShell, onShellMessage, request, command, artUrl } from './bridge'
  import { app, nav, ui, playlist, openSection, back, currentView, newPlaylist, undoToast, toast, receiveLevels, writeSetting, readSetting, currentTrack } from './state.svelte'
  import TransportBar from './parts/TransportBar.svelte'
  import NowPlaying from './parts/NowPlaying.svelte'
  import PlaylistPanel from './parts/PlaylistPanel.svelte'
  import PeopleList from './views/PeopleList.svelte'
  import AlbumsOf from './views/AlbumsOf.svelte'
  import AlbumView from './views/AlbumView.svelte'
  import FolderView from './views/FolderView.svelte'
  import SearchView from './views/SearchView.svelte'
  import FavouritesView from './views/FavouritesView.svelte'
  import ActionStrip from './parts/ActionStrip.svelte'
  import { sortable } from './lib/sortable'
  import Icon from './parts/Icon.svelte'
  import { selection } from './lib/selection.svelte'
  import PlaylistsView from './views/PlaylistsView.svelte'
  import PlayerPicker from './parts/PlayerPicker.svelte'
  import { accentFromCover } from './lib/coverAccent'

  let searchBox = $state<HTMLInputElement>()
  let width = $state(window.innerWidth)
  let height = $state(window.innerHeight)
  const mini = $derived(width < 420 && height < 260)
  const vertical = $derived(!mini && width <= 900)
  const folderMode = $derived(app.librarySettings.AlbumSource !== 'tags')
  const allSections = $derived([['queue', 'Now playing', 'queue'], ['favourites', 'Favourites', 'favourites'], ['playlists', 'Playlists', 'playlists'], ['artists', 'Artists', 'artists'], ['genres', 'Genres', 'genres'], ['composers', 'Composers', 'composers'], ['performers', 'Performers', 'performers'], ['folders', folderMode ? 'Folders' : 'Albums', folderMode ? 'folders' : 'albums']] as [string, string, any][])
  // Sidebar and bottom-bar customisation (one shared setting): every section shows by default; order, spacers and hidden ones are remembered per PC
  // The layout is section keys plus spacers ("spacer-…"); out of the box one spacer sits under the first section
  const isSpacer = (key: string) => key.startsWith('spacer-')
  function initialLayout(): string[] { const stored: string[] = JSON.parse(readSetting('sectionOrder', '[]')); if (readSetting('spacersAdded', '')) return stored; return stored.length ? stored.slice(0, 1).concat(['spacer-1'], stored.slice(1)) : ['queue', 'spacer-1'] }
  let layoutOrder = $state<string[]>(initialLayout())
  // Stored order first (unknown keys dropped), then any section not placed yet
  const layout = $derived(layoutOrder.filter(key => isSpacer(key) || allSections.some(section => section[0] === key)).concat(allSections.map(section => section[0]).filter(key => !layoutOrder.includes(key))))
  const sections = $derived(layout.map(key => isSpacer(key) ? [key, 'Spacer', 'spacer'] : allSections.find(section => section[0] === key)!) as [string, string, any][])
  function saveLayout(order: string[]) { layoutOrder = order; writeSetting('sectionOrder', JSON.stringify(order)); writeSetting('spacersAdded', '1') }
  function changeLayout(order: string[], label: string) { const before = layout.slice(); saveLayout(order); toast(label, () => saveLayout(before)) }
  function moveSection(from: number, to: number) { const keys = layout.slice(), moved = keys.splice(from, 1)[0]; keys.splice(to, 0, moved); changeLayout(keys, `Moved ${isSpacer(moved) ? 'spacer' : sections[from][1]}`) }
  const addSpacer = () => changeLayout(layout.concat([`spacer-${Date.now()}`]), 'Added a spacer at the bottom (drag it into place)')
  const removeSpacer = (key: string) => changeLayout(layout.filter(each => each !== key), 'Removed spacer')
  // Press and hold any sidebar item to start customising
  let holdTimer = 0, heldIntoCustomise = false
  const startHold = () => { clearTimeout(holdTimer); holdTimer = window.setTimeout(() => { customising = true; heldIntoCustomise = true }, 600) }
  const cancelHold = () => clearTimeout(holdTimer)
  let hiddenSections = $state<string[]>(JSON.parse(readSetting('hiddenSections', '[]')))
  let customising = $state(false)
  const toggleSection = (key: string) => { hiddenSections = hiddenSections.includes(key) ? hiddenSections.filter(hidden => hidden !== key) : hiddenSections.concat([key]); writeSetting('hiddenSections', JSON.stringify(hiddenSections)) }
  // Shown entries: hidden sections dropped, and no spacer at the very top, the bottom or twice in a row
  const sidebarEntries = $derived(sections.filter(([key]) => !hiddenSections.includes(key)).filter((entry, position, shown) => !isSpacer(entry[0]) || (position > 0 && position < shown.length - 1 && !isSpacer(shown[position + 1][0]))))
  const visibleSections = $derived(sidebarEntries.filter(([key]) => !isSpacer(key)))
  // Dev builds show their version up top, so every screenshot says which build it is
  const devVersion = $derived(String(app.status?.version ?? '').startsWith('0.') ? 'v' + app.status.version + ' dev' : '')
  const view = $derived(currentView())
  const everything = $derived(ui.search.length >= 2 && (ui.scope === 'everything' || ['queue'].includes(view.kind)))

  onShellMessage('status', message => app.status = message)
  onShellMessage('nowPlaying', message => { app.nowPlaying = message.status; app.receivedAt = Date.now() })
  onShellMessage('players', message => { app.players = message.players ?? []; app.ownPlayer = message.own ?? ''; app.selectedPlayer = message.selected ?? app.selectedPlayer })
  onShellMessage('library', message => app.library = { text: message.text ?? '', done: message.done ?? 0, total: message.total ?? 0 })
  onShellMessage('indexUpdated', () => app.indexVersion++)
  onShellMessage('newPlaylist', () => { if (!playlist.open) newPlaylist(); else playlist.collapsed = false })
  onShellMessage('levels', message => receiveLevels(message.vu, message.peak, message.bands))
  // The skin is chosen in Settings › Appearance and applied live; cached locally so the next start paints in it straight away
  onShellMessage('skin', message => { ui.skin = message.skin || 'auto'; ui.coverAccent = !!message.coverAccent; writeSetting('skin', ui.skin); writeSetting('coverAccent', ui.coverAccent ? '1' : '') })
  onShellMessage('window', message => app.window = { glass: !!message.glass, frameless: !!message.frameless, maximised: !!message.maximised })
  onShellMessage('settings', message => { if (message.library) app.librarySettings = message.library })
  sendToShell({ type: 'ready' })
  // Restore an unsaved playlist draft from last time
  request({ type: 'loadDraft' }).then(draft => { if (draft?.items) newPlaylist(draft.name ?? 'Untitled playlist', draft.items) })

  // Accent from the playing cover, in the frosted skins only (classic and amber keep their hardware colours)
  const coverId = $derived(currentTrack()?.coverid ?? '')
  $effect(() => {
    const cover = coverId, skin = ui.skin, rootStyle = document.documentElement.style
    if (!cover || !ui.coverAccent || !['auto', 'dark', 'light'].includes(skin)) { rootStyle.removeProperty('--accent'); rootStyle.removeProperty('--accent-soft'); return }
    accentFromCover(artUrl(cover, 120)).then(accent => {
      if (cover !== coverId) return
      if (!accent) { rootStyle.removeProperty('--accent'); rootStyle.removeProperty('--accent-soft'); return }
      rootStyle.setProperty('--accent', accent); rootStyle.setProperty('--accent-soft', accent.replace('rgb(', 'rgba(').replace(')', ', 0.15)'))
    })
  })
  // Skins (SPEC.md › Skins): the skin is a data attribute; tokens.css holds the variables
  const prefersDark = window.matchMedia('(prefers-color-scheme: dark)')
  let systemDark = $state(prefersDark.matches)
  prefersDark.addEventListener('change', () => systemDark = prefersDark.matches)
  $effect(() => { document.documentElement.dataset.skin = ui.skin; sendToShell({ type: 'theme', dark: ui.skin === 'auto' ? systemDark : ui.skin !== 'light' }) })
  $effect(() => { document.documentElement.classList.toggle('glass', app.window.glass); document.documentElement.classList.toggle('frameless', app.window.frameless) })
  // Frameless: drag the window from empty parts of the top strip (and the mini player); double-click maximises
  function dragWindow(event: MouseEvent) {
    if (!app.window.frameless || event.button !== 0 || (event.target as HTMLElement).closest('button, input, select, a, [role=slider], img, .no-drag')) return
    if (event.detail === 2) { sendToShell({ type: 'windowControl', action: 'maximise' }); return }
    sendToShell({ type: 'dragWindow' })
  }

  function onKey(event: KeyboardEvent) {
    const typing = (event.target as HTMLElement)?.matches?.('input, textarea, select')
    if (event.key === ' ' && !typing) { event.preventDefault(); command('pause', app.nowPlaying?.mode === 'play' ? '1' : '0') }
    else if (event.ctrlKey && event.key === 'ArrowRight') { event.preventDefault(); command('playlist', 'index', '+1') }
    else if (event.ctrlKey && event.key === 'ArrowLeft') { event.preventDefault(); command('playlist', 'index', '-1') }
    else if (event.ctrlKey && event.key === 'ArrowUp') { event.preventDefault(); command('mixer', 'volume', '+5') }
    else if (event.ctrlKey && event.key === 'ArrowDown') { event.preventDefault(); command('mixer', 'volume', '-5') }
    else if (event.ctrlKey && event.key.toLowerCase() === 'f') { event.preventDefault(); searchBox?.focus(); searchBox?.select() }
    else if (event.ctrlKey && event.key.toLowerCase() === 'n') { event.preventDefault(); if (!playlist.open) newPlaylist(); else playlist.collapsed = false }
    else if (event.ctrlKey && event.key.toLowerCase() === 'z' && !typing && ui.toast?.undo) { event.preventDefault(); undoToast() }
    else if (event.key === 'Escape' && !selection.keys.length) { if (ui.search) ui.search = ''; else if (nav.stack.length) back() }
    else if (event.altKey && event.key === 'ArrowLeft' && nav.stack.length) { event.preventDefault(); back() }
  }
</script>

<svelte:window bind:innerWidth={width} bind:innerHeight={height} onkeydown={onKey} onmouseup={(event) => { if (event.button === 3 && nav.stack.length) back() }} />

{#if mini}
  <div class="mini-shell" role="presentation" onmousedown={dragWindow}><TransportBar mini={true} /></div>
{:else}
  <div class="shell" class:vertical>
    {#if !vertical}
      <nav class="sidebar">
        <div class="brand" role="presentation" onmousedown={dragWindow}>WinSqueeze{#if devVersion}<span class="dev-version">{devVersion}</span>{/if}</div>
        <div class="sidebar-player"><PlayerPicker /></div>
        <div class="section-list" use:sortable={{ onMove: moveSection, holdMs: 150, enabled: customising }}>
        {#each customising ? sections : sidebarEntries as [key, label, glyph] (key)}
          {#if customising && isSpacer(key)}
            <div class="section customise spacer-row" data-sort-row title="Hold and drag to move"><span class="grip">⋮⋮</span><span class="spacer-line"></span><button class="remove-spacer no-hold" onclick={() => removeSpacer(key)} aria-label="Remove spacer">×</button></div>
          {:else if customising}
            <label class="section customise" data-sort-row title="Hold and drag to reorder"><span class="grip">⋮⋮</span><input type="checkbox" checked={!hiddenSections.includes(key)} onchange={() => toggleSection(key)} /><span class="glyph"><Icon name={glyph} /></span>{label}</label>
          {:else if isSpacer(key)}
            <div class="divider"></div>
          {:else}
            <button class="section" class:on={nav.section === key && !everything} onpointerdown={startHold} onpointerup={cancelHold} onpointerleave={cancelHold} onclick={() => { if (heldIntoCustomise) { heldIntoCustomise = false; return } openSection(key) }}><span class="glyph"><Icon name={glyph} /></span>{label}</button>
          {/if}
        {/each}
        </div>
        {#if customising}<button class="customise-toggle" onclick={addSpacer}>＋ Add spacer</button>{/if}
        <button class="customise-toggle" title="Or press and hold any item" onclick={() => { customising = !customising; heldIntoCustomise = false }}>{customising ? 'Done (drag to reorder, tick to show)' : 'Customise sidebar'}</button>
        <button class="section new" onclick={() => playlist.open ? (playlist.collapsed = false) : newPlaylist()}><span class="glyph"><Icon name="plus" /></span>New playlist</button>
        <div class="spacer"></div>
        {#if app.library.total && app.library.done < app.library.total}<div class="sync" title={app.library.text}><div class="bar" style:width={`${(app.library.done / app.library.total) * 100}%`}></div></div>{/if}
        {#if app.library.text}<div class="sync-text">{app.library.text}</div>{/if}
        <button class="section" onclick={() => sendToShell({ type: 'openSettings' })}><span class="glyph"><Icon name="settings" /></span>Settings</button>
      </nav>
    {/if}
    <div class="content-column">
      {#if app.status?.updateReady}
        <div class="update-notice"><span>WinSqueeze {app.status.updateReady} is ready.</span><button class="primary" onclick={() => sendToShell({ type: 'applyUpdate' })}>Restart to update</button></div>
      {/if}
      <header class="topbar" role="presentation" onmousedown={dragWindow}>
        {#if nav.stack.length}<button class="ghost back" onclick={back} aria-label="Back">‹</button>{/if}
        <div class="search">
          <input bind:this={searchBox} bind:value={ui.search} placeholder={ui.scope === 'everything' ? 'Search everything' : 'Search this list'} aria-label="Search" />
          <div class="scope">
            <button class:on={ui.scope === 'list'} onclick={() => ui.scope = 'list'}>This list</button>
            <button class:on={ui.scope === 'everything'} onclick={() => ui.scope = 'everything'}>Everything</button>
          </div>
        </div>
        {#if vertical && devVersion}<span class="dev-version">{devVersion}</span>{/if}
        {#if vertical}<button class="ghost" onclick={() => sendToShell({ type: 'openSettings' })} aria-label="Settings"><Icon name="settings" /></button>{/if}
        {#if app.window.frameless}
          <div class="window-controls">
            <button onclick={() => sendToShell({ type: 'windowControl', action: 'minimise' })} aria-label="Minimise"><Icon name="minimise" size={16} /></button>
            <button onclick={() => sendToShell({ type: 'windowControl', action: 'maximise' })} aria-label={app.window.maximised ? 'Restore' : 'Maximise'}><Icon name={app.window.maximised ? 'restore' : 'maximise'} size={16} /></button>
            <button class="close" onclick={() => sendToShell({ type: 'windowControl', action: 'close' })} aria-label="Close"><Icon name="close" size={16} /></button>
          </div>
        {/if}
      </header>
      <div class="body">
        {#if view.kind === 'queue' && !everything}<main class="content now-playing-host"><NowPlaying /></main>{:else}
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
          {:else if view.kind === 'playlists'}
            <PlaylistsView />
          {:else if view.kind === 'playlist'}
            {#key view.key}<PlaylistsView playlistId={view.key} title={view.title} />{/key}
          {/if}
        </main>{/if}
        <PlaylistPanel />
      </div>
      <ActionStrip />
      <TransportBar />
      {#if vertical}
        <nav class="tabbar">
          {#each visibleSections as [key, label, glyph] (key)}
            <button class:on={nav.section === key} onclick={() => openSection(key)}><span class="glyph"><Icon name={glyph} size={20} /></span><span class="tab-label">{label}</span></button>
          {/each}
        </nav>
      {/if}
    </div>
  </div>
{/if}



<style>
  .shell { display: grid; grid-template-columns: 210px minmax(0, 1fr); height: 100vh; }
  .shell.vertical { grid-template-columns: minmax(0, 1fr); }
  .sidebar { display: flex; flex-direction: column; gap: 2px; padding: 14px 10px; background: var(--glass); backdrop-filter: blur(var(--blur)) saturate(1.4); border-right: 1px solid var(--line); min-height: 0; position: relative; z-index: 20; } /* above the content, so the player menu can overlap it */
  .brand { font-family: var(--font-display); color: var(--readout); text-shadow: 0 0 8px var(--readout-glow); letter-spacing: 0.08em; padding: 4px 10px 14px; }
  .dev-version { display: inline-block; margin-left: 8px; font-family: var(--font-ui); font-size: 10px; letter-spacing: 0; color: var(--fg-quiet); text-shadow: none; border: 1px solid var(--line); border-radius: 6px; padding: 0 5px; vertical-align: middle; white-space: nowrap; }
  .section { display: flex; align-items: center; gap: 10px; background: none; border: 0; color: var(--fg); padding: 8px 10px; border-radius: 8px; cursor: pointer; font: inherit; text-align: left; }
  .section:hover { background: var(--hover); }
  .section.on { background: var(--accent-soft); color: var(--accent); }
  .section-list { display: flex; flex-direction: column; gap: 2px; }
  .customise { cursor: grab; } .grip { color: var(--fg-quiet); font-size: 10px; margin-right: -6px; } .customise input { margin: 0 -4px 0 0; accent-color: var(--accent); }
  .customise-toggle { align-self: flex-start; background: none; border: 0; color: var(--fg-quiet); font: inherit; font-size: 11px; padding: 4px 10px; cursor: pointer; opacity: 0.7; }
  .customise-toggle:hover { opacity: 1; color: var(--accent); }
  .sidebar-player { padding: 0 0 10px; } .sidebar-player :global(.current) { height: 30px; font-size: 12.5px; }
  .divider { height: 1px; background: var(--line); margin: 8px 10px; }
  .spacer-row { min-height: 30px; } .spacer-line { flex: 1; height: 0; border-top: 1px dashed var(--line); }
  .remove-spacer { background: none; border: 0; color: var(--fg-quiet); cursor: pointer; font-size: 16px; padding: 0 4px; } .remove-spacer:hover { color: #ff7a6b; }
  .section.new { color: var(--accent); margin-top: 8px; }
  .glyph { width: 20px; display: grid; place-items: center; }
  .spacer { flex: 1; }
  .sync { height: 3px; background: var(--track); border-radius: 2px; margin: 0 10px; overflow: hidden; }
  .bar { height: 100%; background: var(--accent); }
  .sync-text { color: var(--fg-quiet); font-size: 11px; padding: 4px 10px 8px; }
  .content-column { display: flex; flex-direction: column; min-width: 0; min-height: 0; height: 100vh; }
  .update-notice { display: flex; align-items: center; justify-content: center; gap: 12px; padding: 6px 12px; background: var(--accent-soft); font-size: 13px; }
  .topbar { display: flex; align-items: center; gap: 8px; padding: 10px 16px; }
  .window-controls { display: flex; margin-left: auto; gap: 2px; }
  .window-controls button { display: grid; place-items: center; width: 40px; height: 30px; background: none; border: 0; border-radius: 6px; color: var(--fg-quiet); cursor: pointer; }
  .window-controls button:hover { background: var(--hover); color: var(--fg); }
  .window-controls .close:hover { background: #c42b1c; color: #fff; }
  .back { font-size: 20px; padding: 2px 12px; }
  .search { flex: 1; display: flex; align-items: center; gap: 6px; background: var(--panel); border: 1px solid var(--line); border-radius: 10px; padding: 2px 4px 2px 12px; max-width: 640px; }
  .search input { flex: 1; min-width: 0; background: none; border: 0; outline: none; color: var(--fg); font: inherit; padding: 6px 0; }
  .scope { display: flex; gap: 2px; }
  .scope button { background: none; border: 0; color: var(--fg-quiet); font: inherit; font-size: 12px; border-radius: 7px; padding: 4px 8px; cursor: pointer; white-space: nowrap; }
  .scope button.on { background: var(--accent-soft); color: var(--accent); }
  .body { flex: 1; display: flex; min-height: 0; position: relative; }
  .content { flex: 1; overflow-y: auto; padding: 4px 20px 24px; min-width: 0; }
  .content.now-playing-host { padding: 0; overflow: hidden; }
  @media (max-width: 900px) { .content.now-playing-host { overflow-y: auto; } }
  .tabbar { display: flex; justify-content: space-around; background: var(--glass); backdrop-filter: blur(var(--blur)); border-top: 1px solid var(--line); }
  .tabbar button { flex: 1; display: flex; flex-direction: column; align-items: center; gap: 2px; background: none; border: 0; color: var(--fg-quiet); font: inherit; font-size: 10px; padding: 6px 0; cursor: pointer; min-width: 0; }
  .tabbar button.on { color: var(--accent); }
  .tab-label { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; max-width: 100%; }
  @media (max-width: 480px) { .tab-label { display: none; } .tabbar .glyph { font-size: 18px; } }
  .mini-shell { height: 100vh; background: var(--glass); }
</style>
