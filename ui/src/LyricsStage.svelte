<script lang="ts">
  import { onShellMessage, sendToShell, command, trackArt } from './bridge'
  import { app, currentTrack, readSetting } from './state.svelte'
  import { infoQuery } from './lib/nowPlayingInfo'
  import Icon from './parts/Icon.svelte'
  import LyricsText from './parts/LyricsText.svelte'
  // The lyrics window (SPEC.md › Lyrics view): the playing track's lyrics alone, big, over its blurred cover; full screen or a window of its own
  let fullScreen = $state(false)
  onShellMessage('nowPlaying', message => { app.nowPlaying = message.status; app.receivedAt = Date.now() })
  onShellMessage('lyricsWindow', message => { fullScreen = !!message.fullScreen })
  sendToShell({ type: 'ready' })
  // Same skin as the main window (both pages share their settings)
  const applySkin = () => document.documentElement.dataset.skin = readSetting('skin', 'auto')
  applySkin(); window.addEventListener('storage', applySkin)
  const track = $derived(currentTrack())
  const cover = $derived(trackArt(track, 600))
  const trackKey = $derived(track ? [track.id, track.artist, track.album, track.title].join('|') : '')
  let lyrics = $state<any>(null), lyricsFor = '', scroller = $state<HTMLElement>()
  $effect(() => {
    const key = trackKey, playing = track
    if (key === lyricsFor) return
    lyricsFor = key; lyrics = playing ? 'loading' : null; scroller?.scrollTo({ top: 0 })
    if (playing) infoQuery('lyrics', playing).then(result => { if (lyricsFor === key) lyrics = result ?? { found: false } })
  })
  $effect(() => { document.title = track ? `${track.title} · Lyrics` : 'Lyrics' })
  // Controls fade out while you read, and come back when the mouse moves (the pointer hides too, in full screen)
  let idle = $state(false), idleTimer = 0
  const wake = () => { idle = false; clearTimeout(idleTimer); idleTimer = window.setTimeout(() => idle = true, 2500) }
  wake()
  const toggleFullScreen = () => sendToShell({ type: 'fullScreen', on: !fullScreen })
  function onKey(event: KeyboardEvent) {
    if (event.key === 'Escape' && fullScreen) sendToShell({ type: 'fullScreen', on: false })
    else if (event.key === 'F11') { event.preventDefault(); toggleFullScreen() }
    else if (event.key === ' ') { event.preventDefault(); command('pause', app.nowPlaying?.mode === 'play' ? '1' : '0') }
  }
</script>

<svelte:window onkeydown={onKey} onmousemove={wake} />
<main class="stage" class:idle class:full={fullScreen}>
  {#if cover}<div class="backdrop" style:background-image={`url("${cover}")`}></div>{/if}<div class="veil"></div>
  <header class="track">
    {#if cover}<img src={trackArt(track, 120)} alt="" />{/if}
    <div class="names">{#if track}<div class="title">{track.title}</div><div class="artist">{track.artist ?? ''}</div>{/if}</div>
    <div class="controls">
      <button onclick={toggleFullScreen} title={fullScreen ? 'Leave full screen (Esc)' : 'Full screen (F11)'} aria-label={fullScreen ? 'Leave full screen' : 'Full screen'}><Icon name={fullScreen ? 'exit-full-screen' : 'full-screen'} size={18} /></button>
    </div>
  </header>
  <div class="scroller" bind:this={scroller} ondblclick={(event) => { if (!(event.target as HTMLElement).closest('.line')) toggleFullScreen() }} role="presentation">
    <div class="column">
      {#if !track}<p class="note">Nothing playing</p>
      {:else if lyrics === 'loading'}<p class="note">Looking…</p>
      {:else if !lyrics?.found}<p class="note">No lyrics for this track</p>
      {:else}<LyricsText {lyrics} stage={true} /><div class="source">From {lyrics.source}</div>{/if}
    </div>
  </div>
</main>

<style>
  .stage { position: fixed; inset: 0; overflow: hidden; background: var(--bg); background-image: var(--bg-gradient); color: var(--fg); }
  .backdrop { position: absolute; inset: -120px; background-size: cover; background-position: center; filter: blur(90px) saturate(1.5) brightness(0.55); opacity: 0.8; pointer-events: none; transition: background-image 0.6s ease; }
  :global(:root[data-skin="light"]) .backdrop { filter: blur(90px) saturate(1.3) brightness(1.15); opacity: 0.6; }
  @media (prefers-color-scheme: light) { :global(:root[data-skin="auto"]) .backdrop { filter: blur(90px) saturate(1.3) brightness(1.15); opacity: 0.6; } }
  .veil { position: absolute; inset: 0; pointer-events: none; background: linear-gradient(180deg, color-mix(in srgb, var(--bg) 70%, transparent) 0%, transparent 22%, transparent 70%, color-mix(in srgb, var(--bg) 85%, transparent) 100%); }
  .track { position: absolute; z-index: 2; top: 0; left: 0; right: 0; display: flex; align-items: center; gap: 14px; padding: 22px clamp(20px, 6vw, 96px); }
  .track img { width: 52px; height: 52px; border-radius: 6px; object-fit: cover; box-shadow: 0 8px 24px var(--shadow); }
  .names { min-width: 0; flex: 1; }
  .title { font-family: var(--font-text); font-style: italic; font-size: 21px; line-height: 1.2; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .artist { color: var(--fg-quiet); font-size: 13px; margin-top: 3px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; letter-spacing: 0.02em; }
  .controls { display: flex; gap: 4px; transition: opacity 0.4s ease; }
  .controls button { width: 36px; height: 36px; display: grid; place-items: center; background: none; border: 0; border-radius: 50%; color: var(--fg); opacity: 0.7; cursor: pointer; }
  .controls button:hover { opacity: 1; background: var(--hover); }
  .idle .controls { opacity: 0; } .idle.full { cursor: none; }
  .scroller { position: absolute; inset: 0; overflow-y: auto; scrollbar-width: none; overscroll-behavior: contain; mask-image: linear-gradient(180deg, transparent 0, transparent 92px, #000 210px, #000 calc(100% - 90px), transparent 100%); }
  .column { max-width: 1100px; margin: 0 auto; padding: max(120px, 30vh) clamp(20px, 6vw, 96px) 50vh; font-size: clamp(26px, 3.4vw, 58px); box-sizing: content-box; }
  .note { font-family: var(--font-text); font-style: italic; color: var(--fg-quiet); font-size: 0.7em; margin: 0; }
  .source { margin: 48px 0 0; font-size: 12px; color: var(--fg-quiet); letter-spacing: 0.03em; }
</style>
