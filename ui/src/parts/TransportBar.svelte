<script lang="ts">
  import { command, sendToShell, artUrl } from '../bridge'
  import { app, ui, meters, nav, openSection, currentTrack, formatTime } from '../state.svelte'
  import Analyser from './Analyser.svelte'
  import Readout from './Readout.svelte'
  import FidelityBadge from './FidelityBadge.svelte'
  import Icon from './Icon.svelte'
  import Meters from './Meters.svelte'
  let { mini = false }: { mini?: boolean } = $props()
  let now = $state(Date.now())
  let volumeOpen = $state(false)
  // Mini: the speaker swaps the seek bar for a volume slider until a few seconds after the last change
  let miniVolume = $state(false), miniVolumeTimer = 0
  const showMiniVolume = () => { miniVolume = true; clearTimeout(miniVolumeTimer); miniVolumeTimer = window.setTimeout(() => miniVolume = false, 4000) }
  // The wheel over the mini player steps the volume
  function wheelVolume(event: WheelEvent) { if (!mini) return; const nextVolume = Math.max(0, Math.min(100, volume + (event.deltaY < 0 ? 2 : -2))); draggingVolume = nextVolume; command('mixer', 'volume', nextVolume); clearTimeout(volumeReleaseTimer); volumeReleaseTimer = window.setTimeout(() => draggingVolume = null, 1200); showMiniVolume() }
  $effect(() => { const ticker = setInterval(() => now = Date.now(), 250); return () => clearInterval(ticker) })
  // The art opens Now playing; again goes back to where you were
  let previousSection = 'artists'
  function showNowPlaying() { if (nav.section === 'queue' && !nav.stack.length) openSection(previousSection); else { previousSection = nav.section === 'queue' ? previousSection : nav.section; openSection('queue') } }
  const status = $derived(app.nowPlaying)
  const track = $derived(currentTrack())
  const playing = $derived(status?.mode === 'play')
  const duration = $derived(Number(status?.duration ?? track?.duration ?? 0))
  // Smooth position between ~1 s polls
  const position = $derived(Math.min(duration || Infinity, Number(status?.time ?? 0) + (playing ? (now - app.receivedAt) / 1000 : 0)))
  // While dragging, the slider owns the value and sends it continuously (throttled), so polls don't yank it back
  let draggingVolume = $state<number | null>(null)
  let lastVolumeSent = 0, volumeReleaseTimer = 0
  const volume = $derived(draggingVolume ?? Math.abs(Number(status?.['mixer volume'] ?? 0)))
  function dragVolume(event: Event) {
    clearTimeout(volumeReleaseTimer); if (mini) showMiniVolume(); draggingVolume = Number((event.target as HTMLInputElement).value)
    if (Date.now() - lastVolumeSent > 60) { lastVolumeSent = Date.now(); command('mixer', 'volume', draggingVolume) }
  }
  function releaseVolume(event: Event) { command('mixer', 'volume', (event.target as HTMLInputElement).value); volumeReleaseTimer = window.setTimeout(() => draggingVolume = null, 1200) }
  const ownSelected = $derived(!app.selectedPlayer || app.selectedPlayer === app.ownPlayer)
  // Record (SPEC.md › Recording): only while this PC's player plays a radio station or other stream (never library files), or while a recording runs
  const recording = $derived(app.status?.recording ?? {})
  const showRecord = $derived(recording.available && ownSelected && (recording.on || status?.remote == 1))
  const recordedFor = $derived(recording.on && recording.startedAt ? formatTime((now - recording.startedAt) / 1000) : '')
  const seek = (event: MouseEvent) => { const bar = event.currentTarget as HTMLElement; const ratio = (event.clientX - bar.getBoundingClientRect().left) / bar.clientWidth; if (duration) command('time', Math.round(ratio * duration)) }
</script>

<div class="transport-host" class:mini-host={mini}>
<footer class="transport" class:mini onwheel={wheelVolume}>
  <div class="left">
  <button class="now" onclick={showNowPlaying} title="Now playing">
    {#if track?.coverid}<img src={artUrl(track.coverid, 120)} alt="" />{:else}<span class="no-art">♪</span>{/if}
    <span class="text"><span class="title">{track?.title ?? 'Nothing playing'}</span><span class="artist" title={track?.artist ?? (ownSelected ? app.status?.message ?? '' : '')}>{track?.artist ?? (ownSelected ? app.status?.message ?? '' : '')}</span></span>
  </button>
  {#if showRecord}<button class="record" class:on={recording.on} onclick={() => sendToShell({ type: 'toggleRecording' })} title={recording.on ? `Recording ${recordedFor}${recording.files > 1 ? ` · file ${recording.files}` : ''}. Click to stop` : 'Record this station'} aria-label={recording.on ? 'Stop recording' : 'Record'}><span class="dot"></span>{#if recording.on && !mini}<span class="rec-time">{recordedFor}</span>{/if}</button>{/if}
  </div>
  <div class="centre">
    <div class="buttons">
      <button onclick={() => command('playlist', 'index', '-1')} aria-label="Previous"><Icon name="previous" /></button>
      <button class="play" onclick={() => command('pause', playing ? '1' : '0')} aria-label={playing ? 'Pause' : 'Play'}><Icon name={playing ? 'pause' : 'play'} size={20} /></button>
      <button onclick={() => command('playlist', 'index', '+1')} aria-label="Next"><Icon name="next" /></button>
    </div>
    {#if mini && miniVolume}<div class="seek-row mini-volume"><input type="range" min="0" max="100" value={volume} oninput={dragVolume} onchange={releaseVolume} aria-label="Volume" /><span class="time">{volume}</span></div>
    {:else}<div class="seek-row">
      <span class="time">{formatTime(position)}</span>
      <div class="seek" role="slider" tabindex="0" aria-label="Seek" aria-valuenow={Math.round(position)} onclick={seek} onkeydown={(event) => { if (event.key === 'ArrowRight') command('time', Math.round(position + 10)); if (event.key === 'ArrowLeft') command('time', Math.max(0, Math.round(position - 10))) }}>
        <div class="fill" style:width={duration ? `${(position / duration) * 100}%` : '0'}></div>
      </div>
      <span class="time">{formatTime(duration)}</span>
    </div>{/if}
  </div>
  <div class="right">
    <div class="readout-slot"><Readout /></div>
    <button class="pixel-meter" title="Equaliser" onclick={() => sendToShell({ type: 'openEq' })}><Analyser levels={meters.bands} compact={true} segments={14} /></button>
    <div class="volume">
      <FidelityBadge />
      <input type="range" min="0" max="100" value={volume} oninput={dragVolume} onchange={releaseVolume} aria-label="Volume" />
      <span class="volume-number" class:show={draggingVolume !== null}>{volume}</span>
      <button class="volume-icon" onclick={() => volumeOpen = !volumeOpen} aria-label="Volume"><Icon name="volume" /></button>
      {#if volumeOpen}<div class="volume-pop"><input type="range" min="0" max="100" value={volume} oninput={dragVolume} onchange={releaseVolume} aria-label="Volume" /></div>{/if}
    </div>
    {#if mini}<div class="mini-meters"><Meters size="small" /></div><button class="mini-speaker" class:on={miniVolume} onclick={() => miniVolume ? miniVolume = false : showMiniVolume()} title="Volume (or scroll)"><Icon name="volume" size={16} /></button><button class="pin" class:on={ui.pinned} onclick={() => { ui.pinned = !ui.pinned; sendToShell({ type: 'setTopmost', on: ui.pinned }) }} title="Always on top"><Icon name="pin" size={16} /></button>{/if}
  </div>
</footer>
</div>

<style>
  /* The host is the size container: a container query can't restyle the container itself */
  .transport-host { container-type: inline-size; } .mini-host { height: 100%; }
  .transport { display: grid; grid-template-columns: minmax(0, 1fr) minmax(260px, 1.3fr) minmax(max-content, 1fr); /* equal sides keep the buttons centred; the right never shrinks below its content */ align-items: center; gap: 16px; padding: 10px 16px; background: var(--glass); backdrop-filter: blur(var(--blur)) saturate(1.4); border-top: 1px solid var(--line); }
  .left { display: flex; align-items: center; gap: 8px; min-width: 0; }
  /* Record: a quiet red ring beside a playing station; solid and pulsing while it records */
  .record { display: flex; align-items: center; gap: 6px; flex-shrink: 0; height: 26px; padding: 0 7px; background: none; border: 0; border-radius: 13px; cursor: pointer; color: #e5484d; font: inherit; }
  .record:hover { background: var(--hover); }
  .record .dot { width: 10px; height: 10px; border-radius: 50%; box-sizing: border-box; border: 2px solid currentColor; opacity: 0.75; }
  .record:hover .dot { opacity: 1; }
  .record.on .dot { background: currentColor; opacity: 1; animation: recording-pulse 1.4s ease-in-out infinite; }
  .rec-time { font-size: 11px; font-variant-numeric: tabular-nums; }
  @keyframes recording-pulse { 0%, 100% { box-shadow: 0 0 0 0 color-mix(in srgb, #e5484d 60%, transparent); } 50% { box-shadow: 0 0 0 5px transparent; opacity: 0.55; } }
  .now { display: flex; align-items: center; gap: 10px; min-width: 0; background: none; border: 0; color: var(--fg); cursor: pointer; text-align: left; padding: 0; font: inherit; }
  .now img, .no-art { width: 52px; height: 52px; border-radius: 6px; object-fit: cover; flex-shrink: 0; display: grid; place-items: center; background: var(--panel-strong); box-shadow: 0 2px 10px var(--shadow); }
  .text { display: flex; flex-direction: column; min-width: 0; }
  .title { font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .artist { color: var(--fg-quiet); font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .centre { display: flex; flex-direction: column; align-items: center; gap: 4px; min-width: 0; }
  .buttons { display: flex; gap: 14px; align-items: center; }
  .buttons button { display: grid; place-items: center; padding: 0; background: none; border: 0; color: var(--fg); cursor: pointer; width: 36px; height: 36px; border-radius: 50%; transition: background 120ms, transform 80ms; }
  .buttons button:hover { background: var(--hover); }
  .buttons button:active { transform: scale(0.94); }
  .buttons .play { width: 44px; height: 44px; color: var(--on-accent); background: linear-gradient(180deg, color-mix(in srgb, var(--accent) 100%, white 22%), var(--accent)); box-shadow: 0 4px 14px color-mix(in srgb, var(--accent) 40%, transparent), inset 0 1px 0 rgba(255, 255, 255, 0.35); }
  .buttons .play:hover { background: linear-gradient(180deg, color-mix(in srgb, var(--accent) 100%, white 32%), var(--accent)); }
  .seek-row { display: flex; align-items: center; gap: 8px; width: 100%; }
  .time { color: var(--fg-quiet); font-size: 11px; font-variant-numeric: tabular-nums; min-width: 34px; text-align: center; }
  .seek { flex: 1; height: 5px; background: var(--track); border-radius: 3px; cursor: pointer; position: relative; }
  .seek:hover { height: 7px; }
  .fill { height: 100%; background: var(--accent); border-radius: 3px; }
  .pixel-meter { display: flex; width: 54px; height: 34px; padding: 0; background: none; border: 0; cursor: pointer; border-radius: 5px; } /* the analyser opens the EQ */
  .pixel-meter :global(.analyser) { flex: 1; } .pixel-meter:hover { filter: brightness(1.15); }
  .right { display: flex; align-items: center; justify-content: flex-end; gap: 10px; min-width: 0; }
  .right > * { flex-shrink: 0; }
  .right > .readout-slot { flex-shrink: 1; min-width: 0; overflow: hidden; white-space: nowrap; }
  /* The right column sizes to its content, so it can never spill over the buttons */
  .volume { position: relative; display: flex; align-items: center; }
  .volume input { width: 90px; accent-color: var(--accent); }
  /* the number appears under the slider while hovering or dragging */
  .volume-number { position: absolute; left: 50%; top: calc(50% + 9px); transform: translateX(-50%); color: var(--fg-quiet); font-size: 10px; font-variant-numeric: tabular-nums; opacity: 0; transition: opacity 150ms; pointer-events: none; }
  .volume:hover .volume-number, .volume-number.show { opacity: 1; }
  .volume-icon { display: none; background: none; border: 0; cursor: pointer; color: var(--fg); padding: 4px; }
  .volume-pop { position: absolute; bottom: 36px; right: 0; background: var(--panel-solid); border: 1px solid var(--line); border-radius: 8px; padding: 10px; box-shadow: 0 8px 24px var(--shadow); }
  .pin { background: none; border: 0; cursor: pointer; opacity: 0.5; color: var(--fg); padding: 4px; } .pin.on { opacity: 1; }
  /* Never drop transport controls: collapse the readout, player switcher, seek times, then the volume slider (SPEC.md › Transport) */
  @container (max-width: 1400px) { .readout-slot { display: none; } } /* it's in Now playing too; never let it crowd the buttons */
  @container (max-width: 900px) { .transport { grid-template-columns: minmax(0, 1fr) minmax(200px, 1.4fr) auto; } }
  @container (max-width: 700px) { .time, .pixel-meter { display: none; } }
  @container (max-width: 540px) { .volume > input, .volume-number, .volume :global(.badge) { display: none; } .volume-icon { display: block; } .transport { grid-template-columns: 1fr auto auto; } .seek-row { display: none; } }
  /* Mini: Winamp-style strip */
  .mini { grid-template-columns: 1fr auto; grid-template-rows: auto auto; gap: 6px 10px; padding: 10px; height: 100%; box-sizing: border-box; border: 0; }
  .mini .centre { grid-column: 1 / 3; grid-row: 2; flex-direction: row; }
  .mini .seek-row { display: flex; } .mini .seek-row .time { display: none; }
  .mini .readout-slot, .mini .volume, .mini .pixel-meter { display: none; }
  .mini .right { gap: 4px; }
  .mini-meters { display: flex; transform-origin: right center; } .mini-meters :global(.small .vu) { width: 48px; height: 29px; }
  .mini-speaker { background: none; border: 0; cursor: pointer; color: var(--fg); opacity: 0.6; padding: 4px; } .mini-speaker.on, .mini-speaker:hover { opacity: 1; color: var(--accent); }
  .mini-volume input { flex: 1; accent-color: var(--accent); } .mini .mini-volume .time { display: inline; }
  @container (max-width: 300px) { .mini-meters { display: none; } }
  .mini .now img, .mini .no-art { width: 64px; height: 64px; }
  .mini .buttons { gap: 6px; } .mini .buttons button { width: 30px; height: 30px; } .mini .buttons .play { width: 34px; height: 34px; }
</style>
