<script lang="ts">
  import { t } from './lib/i18n'
  import { checkKeepPlaying } from './lib/queueEnd.svelte'
  import { noteLongTrack } from './lib/resume'
  import { prefetchInfo } from './lib/nowPlayingInfo'
  import { sendToShell, onShellMessage, request, command, trackArt } from './bridge'
  import { app, nav, ui, playlist, openSection, back, forward, forwardHistory, currentView, newPlaylist, pins, togglePin, undoToast, undoLast, redoLast, history, toast, noteTrackChange, receiveLevels, windowMoved, cycleMeterView, setHifi, writeSetting, readSetting, currentTrack } from './state.svelte'
  import TransportBar from './parts/TransportBar.svelte'
  import WhatsNew from './parts/WhatsNew.svelte'
  import TrackInfo from './parts/TrackInfo.svelte'
  import ResizeEdges from './parts/ResizeEdges.svelte'
  import NowPlaying from './parts/NowPlaying.svelte'
  import PlaylistPanel from './parts/PlaylistPanel.svelte'
  import PeopleList from './views/PeopleList.svelte'
  import AlbumsOf from './views/AlbumsOf.svelte'
  import CustomBrowse from './views/CustomBrowse.svelte'
  import CustomViewsEditor from './views/CustomViewsEditor.svelte'
  import AlbumView from './views/AlbumView.svelte'
  import FolderView from './views/FolderView.svelte'
  import SearchView from './views/SearchView.svelte'
  import FavouritesView from './views/FavouritesView.svelte'
  import AppsView from './views/AppsView.svelte'
  import ActionStrip from './parts/ActionStrip.svelte'
  import Meters from './parts/Meters.svelte'
  import HiFi from './parts/HiFi.svelte'
  import { sortable } from './lib/sortable'
  import Icon from './parts/Icon.svelte'
  import { selection, clearSelection } from './lib/selection.svelte'
  import PlaylistsView from './views/PlaylistsView.svelte'
  import DownloadsView from './views/DownloadsView.svelte'
  import { downloads } from './lib/downloads.svelte'
  import HistoryView from './views/HistoryView.svelte'
  import HealthView from './views/HealthView.svelte'
  import PlayerPicker from './parts/PlayerPicker.svelte'
  import { accentFromCover } from './lib/coverAccent'

  let searchBox = $state<HTMLInputElement>()
  let searchView = $state<SearchView>()
  let width = $state(window.innerWidth)
  let height = $state(window.innerHeight)
  const mini = $derived(!ui.hifi && (height < 400 || width < 460)) // short or narrow windows snap to the mini player, with big meters
  const vertical = $derived(!mini && width <= 900)
  const folderMode = $derived(app.librarySettings.AlbumSource !== 'tags')
  // Switched-on custom views (SPEC.md › Custom views): one entry each, or one per group for views that share a group
  const levelGlyphs: Record<string, string> = { genre: 'genres', composer: 'composers', artist: 'artists', performer: 'performers' }
  const customSections = $derived(((app.librarySettings.CustomViews ?? []) as any[]).filter(view => view.Enabled).reduce((entries: string[][], view) => { const key = view.Group ? 'customgroup:' + view.Group : 'custom:' + view.Id; if (!entries.some(entry => entry[0] === key)) entries.push([key, view.Group || view.Name, levelGlyphs[view.Levels?.[view.Levels.length - 1]] ?? 'albums']); return entries }, []))
  const allSections = $derived([['queue', t('Now playing'), 'queue'], ['favourites', t('Favourites'), 'favourites'], ['radio', t('Radio'), 'radio'], ['apps', t('Apps'), 'apps'], ['playlists', t('Playlists'), 'playlists'], ['artists', t('Artists'), 'artists'], ['albumartists', t('Album artists'), 'artists'], ['genres', t('Genres'), 'genres'], ['composers', t('Composers'), 'composers'], ['performers', t('Performers'), 'performers'], ['folders', folderMode ? t('Folders') : t('Albums'), folderMode ? 'folders' : 'albums'], ['compilations', t('Compilations'), 'albums'], ['new', t('New music'), 'new'], ['downloads', t('Downloaded'), 'download'], ['history', t('History'), 'history'], ['health', t('Library health'), 'health']].concat(customSections) as [string, string, any][])
  const pinGlyphs: Record<string, string> = { genre: 'genres', artist: 'artists', composer: 'composers', performer: 'performers', album: 'albums', folder: 'folders', playlist: 'playlists', app: 'apps' }
  const pinSections = $derived(Object.entries(pins.views).map(([key, view]) => [key, view.title ?? '', pinGlyphs[view.kind] ?? 'pin']) as [string, string, any][])
  // Sidebar and bottom-bar customisation (one shared setting): every section shows by default; order, spacers and hidden ones are remembered per PC
  // The layout is section keys plus spacers ("spacer-…"); out of the box one spacer sits under the first section
  const isSpacer = (key: string) => key.startsWith('spacer-')
  // Sections added later arrive next to their neighbour in a sidebar customised before they existed (Radio and Apps by Favourites, Album artists by Artists, Compilations by Albums)
  function initialLayout(): string[] { const stored: string[] = JSON.parse(readSetting('sectionOrder', '[]')); for (const [neighbour, arrivals] of [['favourites', ['radio', 'apps']], ['artists', ['albumartists']], ['folders', ['compilations']]] as [string, string[]][]) if (stored.length && !stored.includes(arrivals[0])) arrivals.slice().reverse().forEach(arrival => stored.splice(stored.includes(neighbour) ? stored.indexOf(neighbour) + 1 : stored.length, 0, arrival)); if (readSetting('spacersAdded', '')) return stored; return stored.length ? stored.slice(0, 1).concat(['spacer-1'], stored.slice(1)) : ['queue', 'spacer-1', 'favourites', 'radio', 'spacer-2'] } // new install: Now playing | Favourites, Radio | the rest
  let layoutOrder = $state<string[]>(initialLayout())
  // Stored order first (unknown keys dropped), then any section not placed yet
  const placeable = $derived(allSections.concat(pinSections))
  const layout = $derived(layoutOrder.filter(key => isSpacer(key) || placeable.some(section => section[0] === key)).concat(placeable.map(section => section[0]).filter(key => !layoutOrder.includes(key))))
  const sections = $derived(layout.map(key => isSpacer(key) ? [key, t('Spacer'), 'spacer'] : placeable.find(section => section[0] === key)!) as [string, string, any][])
  function saveLayout(order: string[]) { layoutOrder = order; writeSetting('sectionOrder', JSON.stringify(order)); writeSetting('spacersAdded', '1') }
  function changeLayout(order: string[], label: string) { const before = layout.slice(); saveLayout(order); toast(label, () => saveLayout(before), 6000, { redo: () => saveLayout(order) }) }
  function moveSection(from: number, to: number) { const keys = layout.slice(), moved = keys.splice(from, 1)[0]; keys.splice(to, 0, moved); changeLayout(keys, isSpacer(moved) ? t('Moved the spacer') : t('Moved {name}', { name: sections[from][1] })) }
  const addSpacer = () => changeLayout(layout.concat([`spacer-${Date.now()}`]), t('Added a spacer at the bottom (drag it into place)'))
  const removeSpacer = (key: string) => changeLayout(layout.filter(each => each !== key), t('Removed spacer'))
  // Press and hold any sidebar item to start customising
  let holdTimer = 0, heldIntoCustomise = false
  const startHold = () => { clearTimeout(holdTimer); holdTimer = window.setTimeout(() => { customising = true; heldIntoCustomise = true }, 600) }
  const cancelHold = () => clearTimeout(holdTimer)
  let hiddenSections = $state<string[]>(JSON.parse(readSetting('hiddenSections', '[]')))
  // Sections that start hidden (Customise sidebar adds them): each is hidden once, the first time this PC sees it
  const startHidden = ['downloads', 'history', 'new', 'albumartists', 'compilations'], hiddenOnce: string[] = JSON.parse(readSetting('hiddenOnce', '[]')), newlyHidden = startHidden.filter(key => !hiddenOnce.includes(key))
  if (newlyHidden.length) { hiddenSections = hiddenSections.concat(newlyHidden.filter(key => !hiddenSections.includes(key))); writeSetting('hiddenSections', JSON.stringify(hiddenSections)); writeSetting('hiddenOnce', JSON.stringify(hiddenOnce.concat(newlyHidden))) }
  let customising = $state(false)
  const toggleSection = (key: string) => { hiddenSections = hiddenSections.includes(key) ? hiddenSections.filter(hidden => hidden !== key) : hiddenSections.concat([key]); writeSetting('hiddenSections', JSON.stringify(hiddenSections)) }
  // Shown entries: hidden sections dropped, and no spacer at the very top, the bottom or twice in a row
  const sidebarEntries = $derived(sections.filter(([key]) => !hiddenSections.includes(key)).filter((entry, position, shown) => !isSpacer(entry[0]) || (position > 0 && position < shown.length - 1 && !isSpacer(shown[position + 1][0]))))
  const visibleSections = $derived(sidebarEntries.filter(([key]) => !isSpacer(key)))
  // Dev builds show their version up top, so every screenshot says which build it is
  const devVersion = $derived(String(app.status?.version ?? '').startsWith('0.') ? 'v' + app.status.version + ' dev' : '')
  const view = $derived(currentView())
  const everything = $derived(ui.search.length >= 2 && (ui.scope === 'everything' || ['queue'].includes(view.kind)))

  // A finished or failed recording says where it went (only notes that arrive after startup)
  let lastRecordingNote = -1
  onShellMessage('status', message => { const recording = message.recording; if (recording && lastRecordingNote >= 0 && recording.noteId > lastRecordingNote && recording.note) toast(recording.note, undefined, 10000, recording.noteSaved && recording.folder ? { action: { label: t('Open in Explorer'), key: 'E', run: () => sendToShell({ type: 'openRecording' }) } } : {}); if (recording) lastRecordingNote = recording.noteId; if (message.offline && !app.status?.offline) openSection('downloads'); if (message.serverBack && !app.status?.serverBack) toast(t('The server is back'), undefined, 15000, { action: { label: t('Go online'), key: 'O', run: () => sendToShell({ type: 'goOnline' }) } }); app.status = message })
  onShellMessage('nowPlaying', message => { noteTrackChange(app.nowPlaying, message.status); noteLongTrack(app.nowPlaying, message.status); app.nowPlaying = message.status; checkKeepPlaying(message.status); app.receivedAt = Date.now() })
  onShellMessage('players', message => { app.players = message.players ?? []; app.syncGroups = message.syncGroups ?? []; app.groupVolumes = message.groupVolumes ?? {}; app.ownPlayer = message.own ?? ''; app.selectedPlayer = message.selected ?? app.selectedPlayer })
  onShellMessage('library', message => app.library = { text: message.text ?? '', done: message.done ?? 0, total: message.total ?? 0 })
  onShellMessage('indexUpdated', () => app.indexVersion++)
  onShellMessage('openSection', message => openSection(message.section))
  onShellMessage('newPlaylist', () => { if (!playlist.open) newPlaylist(); else playlist.collapsed = false })
  onShellMessage('levels', message => receiveLevels(message.vu, message.peak, message.bands))
  onShellMessage('meterSettings', message => { app.meterSettings = { referenceDbfs: message.referenceDbfs, autoReference: message.autoReference, styles: Array.isArray(message.styles) && message.styles.length ? message.styles : ['vu'] }; if (!app.meterSettings.styles.includes(ui.meterStyle)) { ui.meterStyle = app.meterSettings.styles[0]; writeSetting('meterStyle', ui.meterStyle) } })
  // The skin is chosen in Settings › Appearance and applied live; cached locally so the next start paints in it straight away
  onShellMessage('skin', message => {
    ui.skin = message.skin || 'auto'; ui.coverAccent = !!message.coverAccent; ui.coverBackdrop = message.coverBackdrop !== false; ui.accent = message.accent ?? ''; ui.glassTransparency = message.glassTransparency ?? -1; ui.glassTintHue = message.glassTintHue ?? 0; ui.lookVersion++ // every skin message repaints the glass, even when nothing changed
    writeSetting('skin', ui.skin); writeSetting('coverAccent', ui.coverAccent ? '1' : ''); writeSetting('coverBackdrop', ui.coverBackdrop ? '1' : '0'); writeSetting('accent', ui.accent); writeSetting('glassTransparency', String(ui.glassTransparency)); writeSetting('glassTintHue', String(ui.glassTintHue))
  })
  onShellMessage('window', message => app.window = { glass: !!message.glass, frameless: !!message.frameless, maximised: !!message.maximised })
  onShellMessage('windowMoved', message => windowMoved(Number(message.x)))
  onShellMessage('settings', message => { if (message.library) app.librarySettings = message.library; if (message.hints) app.hints = message.hints; if (message.playback) app.playback = message.playback; if (Array.isArray(message.trackInfo)) app.trackInfoTabs = message.trackInfo })
  sendToShell({ type: 'ready' })
  if (readSetting('hifiUpdateFrom', '')) setHifi(true) // updated from hi-fi mode: come back as the unit, which says what it was updated to
  // Restore an unsaved playlist draft from last time
  request({ type: 'loadDraft' }).then(draft => { if (draft?.items) newPlaylist(draft.name ?? t('Untitled playlist'), draft.items) })

  // Accent (Settings › Appearance): the playing cover's colour when that's on (frosted skins), else the chosen colour, else the skin's own blue
  const coverArt = $derived(trackArt(currentTrack(), 120))
  function applyAccent(accent: string) {
    const rootStyle = document.documentElement.style
    if (!accent) { for (const name of ['--accent', '--accent-soft', '--on-accent']) rootStyle.removeProperty(name); return }
    const channels = accent.startsWith('#') ? [1, 3, 5].map(start => parseInt(accent.slice(start, start + 2), 16)) : (accent.match(/\d+/g) ?? []).slice(0, 3).map(Number)
    rootStyle.setProperty('--accent', `rgb(${channels.join(', ')})`); rootStyle.setProperty('--accent-soft', `rgba(${channels.join(', ')}, 0.15)`)
    // Text on the accent: dark on light accents, white on dark ones
    rootStyle.setProperty('--on-accent', 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2] > 150 ? '#06141c' : '#ffffff')
  }
  $effect(() => {
    const cover = coverArt, chosen = ui.accent
    if (!cover || !ui.coverAccent || !['auto', 'dark', 'light'].includes(ui.skin)) { applyAccent(chosen); return }
    accentFromCover(cover).then(accent => { if (cover === coverArt) applyAccent(accent || chosen) })
  })
  // Glass tint (frosted skins with glass): transparency and hue from Appearance; the mini player keeps its lighter share of the tint
  $effect(() => {
    const rootStyle = document.documentElement.style, light = ui.skin === 'light' || (ui.skin === 'auto' && !systemDark)
    ui.lookVersion
    if (!['auto', 'dark', 'light'].includes(ui.skin)) { rootStyle.removeProperty('--glass-tint'); rootStyle.removeProperty('--glass-tint-mini'); return }
    const defaultTint = ui.glassTransparency < 0 && !ui.glassTintHue
    const opacity = ui.glassTransparency < 0 ? (light ? 0.42 : 0.38) : 1 - ui.glassTransparency / 100
    const colour = ui.glassTintHue ? (light ? `hsl(${ui.glassTintHue} 45% 92%` : `hsl(${ui.glassTintHue} 40% 12%`) : (light ? 'rgb(242 245 250' : 'rgb(12 14 19')
    // A nudge then the real value next frame forces a fresh paint of the glass (a stale solid frame after an update only cleared when the value changed)
    rootStyle.setProperty('--glass-tint', `${colour} / ${(opacity + 0.004).toFixed(3)})`)
    requestAnimationFrame(() => { if (defaultTint) { rootStyle.removeProperty('--glass-tint'); rootStyle.removeProperty('--glass-tint-mini'); return } rootStyle.setProperty('--glass-tint', `${colour} / ${opacity.toFixed(2)})`); rootStyle.setProperty('--glass-tint-mini', `${colour} / ${(opacity * 0.37).toFixed(2)})`) })
  })
  // Skins (SPEC.md › Skins): the skin is a data attribute; tokens.css holds the variables
  const prefersDark = window.matchMedia('(prefers-color-scheme: dark)')
  let systemDark = $state(prefersDark.matches)
  prefersDark.addEventListener('change', () => systemDark = prefersDark.matches)
  $effect(() => prefetchInfo(currentTrack(), app.trackInfoTabs)) // Artist, Album and Lyrics looked up as each track starts, ready before their tabs open
  $effect(() => { document.documentElement.dataset.skin = ui.skin; sendToShell({ type: 'theme', dark: ui.skin === 'auto' ? systemDark : ui.skin !== 'light' }) })
  // Tell the shell the page's edge colour, so the frameless border matches it exactly (after the classes and skin have applied)
  $effect(() => { ui.skin; ui.glassTransparency; ui.glassTintHue; ui.lookVersion; systemDark; app.window.glass; app.window.frameless; mini; requestAnimationFrame(() => sendToShell({ type: 'edgeColour', colour: getComputedStyle(document.body).backgroundColor })) })
  let miniUpdateOpen = $state(false)
  $effect(() => { document.documentElement.classList.toggle('mini-mode', mini); document.documentElement.classList.toggle('maximised', app.window.maximised) })
  $effect(() => { document.documentElement.classList.toggle('glass', app.window.glass); document.documentElement.classList.toggle('frameless', app.window.frameless) })
  // Selected text: Ctrl+C or right-click › Copy (the browser's own menu is off)
  let copyMenu = $state<{ left: number; top: number } | null>(null)
  const copySelection = () => { navigator.clipboard.writeText(getSelection()?.toString() ?? ''); copyMenu = null }
  function onContextMenu(event: MouseEvent) {
    if (event.defaultPrevented || (event.target as HTMLElement).matches?.('input, textarea')) return
    if (!getSelection()?.toString()) { copyMenu = null; return }
    event.preventDefault(); copyMenu = { left: Math.min(event.clientX, window.innerWidth - 100), top: Math.min(event.clientY, window.innerHeight - 40) }
  }
  // Frameless: drag the window from empty parts of the top strip (and the mini player); double-click maximises
  function dragWindow(event: MouseEvent) {
    // The native drag swallows the click, so clear any text selection here, as a click elsewhere normally would
    if (event.button === 0 && !(event.target as HTMLElement).closest('.text, h1, input')) getSelection()?.removeAllRanges()
    if (!app.window.frameless || event.button !== 0 || (event.target as HTMLElement).closest('button, input, select, a, [role=slider], img, .no-drag')) return
    if (event.detail === 2) { sendToShell({ type: 'windowControl', action: 'maximise' }); return }
    sendToShell({ type: 'dragWindow' })
  }

  function escapeLayer(event: KeyboardEvent) {
    if (event.defaultPrevented) return
    if (copyMenu) copyMenu = null
    else if (customising) { customising = false; heldIntoCustomise = false }
    else if (ui.trackInfo || ui.itemInfo) { ui.trackInfo = null; ui.itemInfo = null }
    else if (ui.hifi) setHifi(false)
    else if (ui.metersFull) ui.metersFull = false
    else if (selection.keys.length) clearSelection()
    else if (ui.toast) ui.toast = null
    else if (ui.search) ui.search = ''
    else if (nav.stack.length) back()
  }
  function onKey(event: KeyboardEvent) {
    const typing = (event.target as HTMLElement)?.matches?.('input, textarea, select')
    // Esc closes one layer per press, innermost first. Menus, dropdowns and drags close themselves and preventDefault; this runs after them
    if (event.key === 'Escape') { setTimeout(() => escapeLayer(event)); return }
    if (event.key === ' ' && !typing) { event.preventDefault(); command('pause', app.nowPlaying?.mode === 'play' ? '1' : '0') }
    else if (event.ctrlKey && event.key === 'ArrowRight') { event.preventDefault(); command('playlist', 'index', '+1') }
    else if (event.ctrlKey && event.key === 'ArrowLeft') { event.preventDefault(); command('playlist', 'index', '-1') }
    else if (event.ctrlKey && event.key === 'ArrowUp') { event.preventDefault(); command('mixer', 'volume', '+5') }
    else if (event.ctrlKey && event.key === 'ArrowDown') { event.preventDefault(); command('mixer', 'volume', '-5') }
    else if (event.ctrlKey && event.key.toLowerCase() === 'f') { event.preventDefault(); searchBox?.focus(); searchBox?.select() }
    else if (event.ctrlKey && event.key.toLowerCase() === 'n') { event.preventDefault(); if (!playlist.open) newPlaylist(); else playlist.collapsed = false }
    else if (event.ctrlKey && event.key.toLowerCase() === 'c' && !typing && getSelection()?.toString()) { event.preventDefault(); copySelection() }
    else if (event.ctrlKey && ((event.shiftKey && event.key.toLowerCase() === 'z') || event.key.toLowerCase() === 'y') && !typing) { event.preventDefault(); redoLast() }
    else if (event.ctrlKey && event.key.toLowerCase() === 'z' && !typing) { event.preventDefault(); if (ui.toast?.undo && ui.toast.undo !== undoLast) undoToast(); else undoLast() }
    else if (!event.ctrlKey && !event.altKey && !typing && ui.toast?.action && event.key.toUpperCase() === ui.toast.action.key) { event.preventDefault(); const action = ui.toast.action; ui.toast = null; action.run() }
    else if (event.altKey && event.key === 'ArrowLeft' && nav.stack.length) { event.preventDefault(); back() }
    else if (event.altKey && event.key === 'ArrowRight' && forwardHistory.views.length) { event.preventDefault(); forward() }
  }
  // Full-window meters: the normal large meters, zoomed evenly to fill the window (everything scales together, like a bigger copy)
  let fullWidth = $state(0), fullHeight = $state(0), fullMeters = $state<HTMLElement>(), fullZoom = $state(1)
  function fitFullMeters() { if (!fullMeters) return; const rect = fullMeters.getBoundingClientRect(), naturalWidth = rect.width / fullZoom, naturalHeight = rect.height / fullZoom; if (naturalWidth && naturalHeight) fullZoom = Math.max(1, Math.min((fullWidth - 80) / naturalWidth, (fullHeight - 100) / naturalHeight)) }
  $effect(() => { fullMeters; fullWidth; fullHeight; ui.meterStyle; requestAnimationFrame(fitFullMeters) })
</script>

<svelte:window bind:innerWidth={width} bind:innerHeight={height} onkeydown={onKey} onmouseup={(event) => { if (event.button === 3 && nav.stack.length) back(); if (event.button === 4) forward() }} oncontextmenu={onContextMenu} onpointerdown={(event) => { if (copyMenu && !(event.target as HTMLElement).closest('.copy-menu')) copyMenu = null }} />
<WhatsNew {mini} offset={!!app.status?.updateReady} />
{#if (ui.trackInfo || ui.itemInfo) && !mini}<TrackInfo ids={ui.trackInfo ?? []} items={ui.itemInfo} />{/if}
{#if ui.metersFull && !mini}<!-- svelte-ignore a11y_no_noninteractive_element_interactions --><!-- blank frosted areas drag the window, like the meters themselves -->
<div class="meters-full" role="dialog" aria-label={t('Meters')} tabindex="-1" oncontextmenu={(event) => { event.preventDefault(); cycleMeterView() }} onmousedown={(event) => { if (event.button === 0 && !(event.target as HTMLElement).closest("button")) sendToShell({ type: "dragWindow" }) }} bind:clientWidth={fullWidth} bind:clientHeight={fullHeight}><div class="meters-zoom" bind:this={fullMeters} style:zoom={fullZoom}><Meters size="large" zoom={fullZoom} moveWindow={true} /></div><button class="meters-full-close" onclick={() => ui.metersFull = false} title={t('Back to the player (Esc)')} aria-label={t('Back to the player')}><Icon name="return" /></button></div>{/if}
{#if ui.hifi}<HiFi />{/if}
{#if app.window.frameless && !app.window.maximised}<ResizeEdges />{/if}
{#if copyMenu}<div class="copy-menu" style:left="{copyMenu.left}px" style:top="{copyMenu.top}px"><button onclick={copySelection}>{t('Copy')}</button></div>{/if}

{#if mini}
  <div class="mini-shell" role="presentation" onmousedown={dragWindow}>
    <TransportBar mini={true} />
    <!-- Update ready: a pulsing dot in the corner; clicking it slides the restart notice down over the player -->
    {#if app.status?.updateReady}
      <button class="update-dot" onclick={() => miniUpdateOpen = !miniUpdateOpen} aria-label={t('Update ready')} title={t('WinSqueeze {version} is ready', { version: app.status.updateReady })}></button>
      <div class="mini-update" class:open={miniUpdateOpen}><span>{t('Version {version} is ready', { version: app.status.updateReady })}</span><button class="primary" onclick={() => sendToShell({ type: 'applyUpdate' })}>{t('Restart to update')}</button><button class="ghost" onclick={() => miniUpdateOpen = false} aria-label={t('Later')}>×</button></div>
    {/if}
  </div>
{:else}
  <div class="shell" class:vertical>
    {#if !vertical}
      <nav class="sidebar">
        <div class="brand" role="presentation" onmousedown={dragWindow}>WinSqueeze{#if devVersion}<span class="dev-version">{devVersion}</span>{/if}</div>
        <div class="sidebar-player"><PlayerPicker /></div>
        <div class="section-list" use:sortable={{ onMove: moveSection, holdMs: 150, enabled: customising }}>
        {#each customising ? sections : sidebarEntries as [key, label, glyph] (key)}
          {#if customising && isSpacer(key)}
            <div class="section customise spacer-row" data-sort-row title={t('Hold and drag to move')}><span class="grip">⋮⋮</span><span class="spacer-line"></span><button class="remove-spacer no-hold" onclick={() => removeSpacer(key)} aria-label={t('Remove spacer')}>×</button></div>
          {:else if customising}
            <label class="section customise" data-sort-row title={t('Hold and drag to reorder')}><span class="grip">⋮⋮</span><input type="checkbox" checked={!hiddenSections.includes(key)} onchange={() => toggleSection(key)} /><span class="glyph"><Icon name={glyph} /></span><span class="customise-label">{label}</span>{#if pins.views[key]}<button class="remove-spacer no-hold" onclick={(event) => { event.preventDefault(); togglePin(pins.views[key]) }} aria-label={t('Unpin')} title={t('Unpin')}>×</button>{/if}</label>
          {:else if isSpacer(key)}
            <div class="divider"></div>
          {:else}
            <button class="section" class:on={nav.section === key && !everything} onpointerdown={startHold} onpointerup={cancelHold} onpointerleave={cancelHold} onclick={() => { if (heldIntoCustomise) { heldIntoCustomise = false; return } openSection(key) }}><span class="glyph"><Icon name={glyph} /></span>{label}{#if key === 'downloads' && downloads.active.length}<span class="section-badge" title={t('Downloading')}>{downloads.active.reduce((sum, group) => sum + group.total - group.done, 0)}</span>{/if}</button>
          {/if}
        {/each}
        </div>
        {#if customising}<button class="customise-toggle" onclick={addSpacer}>{t('＋ Add spacer')}</button><div class="customise-actions"><button class="ghost" onclick={() => { customising = false; openSection('customViews') }}>{t('Custom views')}</button><button class="primary" onclick={() => { customising = false; heldIntoCustomise = false }}>{t('Done')}</button></div>
        {:else}<button class="customise-toggle" title={t('Or press and hold any item')} onclick={() => { customising = true; heldIntoCustomise = false }}>{t('Customise sidebar')}</button>{/if}
        <button class="section new" onclick={() => playlist.open ? (playlist.collapsed = false) : newPlaylist()}><span class="glyph"><Icon name="plus" /></span>{t('New playlist')}</button>
        <div class="spacer"></div>
        {#if app.library.total && app.library.done < app.library.total}<div class="sync" title={app.library.text}><div class="bar" style:width={`${(app.library.done / app.library.total) * 100}%`}></div></div>{/if}
        {#if app.library.text}<div class="sync-text">{app.library.text}</div>{/if}
        <button class="section" onclick={() => sendToShell({ type: 'openSettings' })}><span class="glyph"><Icon name="settings" /></span>{t('Settings')}</button>
      </nav>
    {/if}
    <div class="content-column">
      {#if app.status?.updateReady}
        <div class="update-notice"><span>{t('WinSqueeze {version} is ready.', { version: app.status.updateReady })}</span><button class="primary" onclick={() => sendToShell({ type: 'applyUpdate' })}>{t('Restart to update')}</button></div>
      {/if}
      <header class="topbar" role="presentation" onmousedown={dragWindow}>
        {#if nav.stack.length || forwardHistory.views.length}<div class="history"><button class="ghost back" onclick={back} disabled={!nav.stack.length} aria-label={t('Back')} title={t('Back (Alt+Left)')}><Icon name="back" size={18} /></button><button class="ghost back" onclick={forward} disabled={!forwardHistory.views.length} aria-label={t('Forward')} title={t('Forward (Alt+Right)')}><Icon name="forward" size={18} /></button></div>{/if}
        <div class="search">
          <input bind:this={searchBox} bind:value={ui.search} onkeydown={(event) => { if (event.key === 'ArrowDown' && everything && searchView) { event.preventDefault(); searchView.focusFirstResult() } }} placeholder={ui.scope === 'everything' ? t('Search everything') : t('Search this list')} aria-label={t('Search')} />
          <div class="scope">
            <button class:on={ui.scope === 'list'} onclick={() => ui.scope = 'list'}>{t('This list')}</button>
            <button class:on={ui.scope === 'everything'} onclick={() => ui.scope = 'everything'}>{t('Everything')}</button>
          </div>
        </div>
        {#if vertical && devVersion}<span class="dev-version">{devVersion}</span>{/if}
        {#if vertical}<button class="ghost" onclick={() => sendToShell({ type: 'openSettings' })} aria-label={t('Settings')}><Icon name="settings" /></button>{/if}
        <!-- Always on top: the same pin as the mini player -->
        <button class="pin-top" class:on={ui.pinned} onclick={() => { ui.pinned = !ui.pinned; sendToShell({ type: 'setTopmost', on: ui.pinned }) }} aria-pressed={ui.pinned} title={ui.pinned ? t('Always on top: on') : t('Always on top: off')}><Icon name="pin" size={15} /></button>
        {#if app.window.frameless}
          <div class="window-controls">
            <button onclick={() => sendToShell({ type: 'windowControl', action: 'minimise' })} aria-label={t('Minimise')}><Icon name="minimise" size={16} /></button>
            <button onclick={() => sendToShell({ type: 'windowControl', action: 'maximise' })} aria-label={app.window.maximised ? t('Restore') : t('Maximise')}><Icon name={app.window.maximised ? 'restore' : 'maximise'} size={16} /></button>
            <button class="close" onclick={() => sendToShell({ type: 'windowControl', action: 'close' })} aria-label={t('Close')}><Icon name="close" size={16} /></button>
          </div>
        {/if}
      </header>
      <div class="body">
        {#if view.kind === 'queue' && !everything}<main class="content now-playing-host"><NowPlaying /></main>{:else}
        <main class="content">
          {#if everything}
            <SearchView bind:this={searchView} {searchBox} />
          {:else if view.kind === 'artists' || view.kind === 'albumartists' || view.kind === 'composers' || view.kind === 'performers' || view.kind === 'genres'}
            {#key view.kind}<PeopleList kind={view.kind} title={({ artists: t('Artists'), albumartists: t('Album artists'), composers: t('Composers'), performers: t('Performers'), genres: t('Genres') } as Record<string, string>)[view.kind]} />{/key}
          {:else if view.kind === 'new'}
            <AlbumsOf kind="new" title={t('New music')} />
          {:else if view.kind === 'compilations'}
            <AlbumsOf kind="compilations" title={t('Compilations')} />
          {:else if view.kind === 'folders'}
            {#if folderMode}<FolderView />{:else}<AlbumsOf kind="albums" title={t('Albums')} />{/if}
          {:else if view.kind === 'folder'}
            {#key view.key}<FolderView folderKey={view.key} />{/key}
          {:else if view.kind === 'album'}
            {#key view.key}<AlbumView albumKey={view.key ?? ''} highlight={view.highlight} />{/key}
          {:else if view.kind === 'artist' || view.kind === 'composer' || view.kind === 'performer' || view.kind === 'genre'}
            {#key view.kind + view.key}<AlbumsOf kind={view.kind} key={view.key} title={view.title} />{/key}
          {:else if view.kind === 'custom' || view.kind === 'customGroup'}
            {#key view.kind + view.key + JSON.stringify(view.filters ?? [])}<CustomBrowse viewId={view.kind === 'custom' ? view.key : ''} group={view.kind === 'customGroup' ? view.key : ''} filters={view.filters ?? []} />{/key}
          {:else if view.kind === 'customViews'}
            <CustomViewsEditor />
          {:else if view.kind === 'favourites'}
            {#key view.key}<FavouritesView itemId={view.key ?? ''} title={view.title ?? ''} />{/key}
          {:else if view.kind === 'radio' || view.kind === 'apps' || view.kind === 'app' || view.kind === 'app-search'}
            {#key view.kind + view.key}<AppsView kind={view.kind} key={view.key ?? ''} title={view.title ?? ''} />{/key}
          {:else if view.kind === 'history'}
            <HistoryView />
          {:else if view.kind === 'health'}
            <HealthView />
          {:else if view.kind === 'downloads'}
            <DownloadsView />
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
  .section-badge { margin-left: auto; min-width: 18px; padding: 0 5px; border-radius: 9px; background: var(--accent); color: var(--on-accent); font-size: 10.5px; line-height: 18px; text-align: center; }
  .section-list { display: flex; flex-direction: column; gap: 2px; flex: 0 1 auto; min-height: 0; overflow-y: auto; overflow-x: hidden; scrollbar-width: thin; } /* scrolls when the window is too short; the buttons below stay put */
  .customise { cursor: grab; } .grip { color: var(--fg-quiet); font-size: 10px; margin-right: -6px; } .customise input { margin: 0 -4px 0 0; accent-color: var(--accent); }
  .customise-toggle { align-self: flex-start; background: none; border: 0; color: var(--fg-quiet); font: inherit; font-size: 11px; padding: 4px 10px; cursor: pointer; opacity: 0.7; }
  .customise-toggle:hover { opacity: 1; color: var(--accent); }
  .customise-actions { display: flex; flex-wrap: wrap; gap: 8px; padding: 8px 10px 4px; } .customise-actions button { flex: 1 1 auto; white-space: nowrap; padding: 6px 8px; }
  .customise-label { min-width: 0; overflow-wrap: anywhere; hyphens: auto; } /* long compound words (Albuminterpreten) break inside the narrow sidebar while customising */
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
  .window-controls { display: flex; gap: 2px; } /* follows the pin, which takes the free space */
  .window-controls button { display: grid; place-items: center; width: 40px; height: 30px; background: none; border: 0; border-radius: 6px; color: var(--fg-quiet); cursor: pointer; }
  .window-controls button:hover { background: var(--hover); color: var(--fg); }
  .window-controls .close:hover { background: #c42b1c; color: #fff; }
  .history { display: flex; gap: 2px; }
  .back { display: grid; place-items: center; width: 34px; height: 32px; padding: 0; }
  .back:disabled { opacity: 0.3; cursor: default; }
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
  .tab-label { max-width: 100%; text-align: center; line-height: 1.15; overflow-wrap: break-word; display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 2; line-clamp: 2; overflow: hidden; } /* two short lines rather than cutting a longer translation off */
  @media (max-width: 480px) { .tab-label { display: none; } .tabbar .glyph { font-size: 18px; } }
  .pin-top { display: grid; place-items: center; width: 34px; height: 30px; margin-left: auto; background: none; border: 0; border-radius: 6px; color: var(--fg); opacity: 0.45; cursor: pointer; transform: rotate(45deg); transition: opacity 120ms, transform 120ms, background 120ms; }
  .pin-top:hover { opacity: 0.8; background: var(--hover); }
  .pin-top.on { opacity: 1; color: var(--accent); background: var(--accent-soft); transform: none; } /* upright and lit when pinned */
  .update-dot { position: absolute; top: 5px; right: 5px; z-index: 3; width: 9px; height: 9px; padding: 0; border: 0; border-radius: 50%; background: var(--accent); cursor: pointer; animation: update-pulse 1.8s ease-in-out infinite; }
  .update-dot::before { content: ""; position: absolute; inset: -6px; border-radius: 50%; } /* bigger hit area */
  @keyframes update-pulse { 0%, 100% { box-shadow: 0 0 0 0 color-mix(in srgb, var(--accent) 70%, transparent); } 60% { box-shadow: 0 0 0 7px color-mix(in srgb, var(--accent) 0%, transparent); } }
  .mini-update { position: absolute; left: 0; right: 0; top: 0; z-index: 4; display: flex; align-items: center; gap: 10px; padding: 10px 12px; font-size: 13px; background: color-mix(in srgb, var(--accent-soft) 60%, rgba(14, 16, 22, 0.92)); backdrop-filter: blur(18px); box-shadow: 0 4px 14px rgba(0, 0, 0, 0.35); transform: translateY(-110%); transition: transform 260ms cubic-bezier(0.2, 0.8, 0.2, 1); }
  .mini-update.open { transform: translateY(0); }
  .mini-update span { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .mini-shell { height: 100vh; background: var(--glass); position: relative; }
  /* Mini on glass: a light tint so the desktop shows through Windows' blur, plus a fine frosted grain */
  :global(:root.glass.mini-mode body) { background: var(--glass-tint-mini, rgba(12, 14, 19, 0.14)); }
  :global(:root.glass[data-skin="light"].mini-mode body) { background: var(--glass-tint-mini, rgba(245, 247, 250, 0.2)); }
  :global(:root.glass) .mini-shell { background: transparent; }
  :global(:root.glass) .mini-shell::before { content: ""; position: absolute; inset: 0; pointer-events: none; opacity: 0.07; mix-blend-mode: overlay; background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='160' height='160'%3E%3Cfilter id='grain'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='2' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23grain)'/%3E%3C/svg%3E"); }
  :global(:root.glass) .mini-shell :global(.transport) { background: transparent; backdrop-filter: none; }
  .mini-shell :global(.transport-host) { position: relative; z-index: 1; } /* above the grain */
  :global(:root.glass) .mini-shell :global(.text) { text-shadow: 0 1px 3px rgba(0, 0, 0, 0.45); }
  :global(:root.glass[data-skin="light"]) .mini-shell :global(.text) { text-shadow: 0 1px 2px rgba(255, 255, 255, 0.6); }
  .copy-menu { position: fixed; z-index: 100; background: var(--panel-solid); border: 1px solid var(--line); border-radius: 8px; padding: 4px; box-shadow: 0 8px 24px var(--shadow); }
  .copy-menu button { display: block; min-width: 90px; text-align: left; background: none; border: 0; color: var(--fg); font: inherit; padding: 6px 10px; border-radius: 5px; cursor: pointer; }
  .copy-menu button:hover { background: var(--hover); }
  /* Right-click on the meters: they fill the window, like Task Manager's graphs */
  .meters-full { position: fixed; inset: 0; z-index: 85; outline: none; display: grid; place-items: center; background: color-mix(in srgb, var(--panel-solid) 88%, transparent); backdrop-filter: blur(30px) saturate(1.3); }
  .meters-full-close { position: absolute; top: 14px; right: 14px; width: 34px; height: 34px; border: 0; border-radius: 8px; background: var(--hover); color: var(--fg); display: grid; place-items: center; cursor: pointer; }
</style>
