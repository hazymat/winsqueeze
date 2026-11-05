<script lang="ts">
  import { command, sendToShell, artUrl } from '../bridge'
  import { app, ui, currentTrack, formatTime } from '../state.svelte'
  import Meters from './Meters.svelte'
  import Readout from './Readout.svelte'
  import Icon from './Icon.svelte'
  let { mini = false }: { mini?: boolean } = $props()
  let now = $state(Date.now())
  let volumeOpen = $state(false)
  $effect(() => { const ticker = setInterval(() => now = Date.now(), 250); return () => clearInterval(ticker) })
  const status = $derived(app.nowPlaying)
  const track = $derived(currentTrack())
  const playing = $derived(status?.mode === 'play')
  const duration = $derived(Number(status?.duration ?? track?.duration ?? 0))
  // Smooth position between ~1 s polls
  const position = $derived(Math.min(duration || Infinity, Number(status?.time ?? 0) + (playing ? (now - app.receivedAt) / 1000 : 0)))
  const volume = $derived(Math.abs(Number(status?.['mixer volume'] ?? 0)))
  const ownSelected = $derived(!app.selectedPlayer || app.selectedPlayer === app.ownPlayer)
  const seek = (event: MouseEvent) => { const bar = event.currentTarget as HTMLElement; const ratio = (event.clientX - bar.getBoundingClientRect().left) / bar.clientWidth; if (duration) command('time', Math.round(ratio * duration)) }
  const selectPlayer = (event: Event) => { app.selectedPlayer = (event.target as HTMLSelectElement).value; sendToShell({ type: 'selectPlayer', id: app.selectedPlayer }) }
</script>

<footer class="transport" class:mini>
  <button class="now" onclick={() => ui.expanded = !ui.expanded} title="Now playing">
    {#if track?.coverid}<img src={artUrl(track.coverid, 120)} alt="" />{:else}<span class="no-art">♪</span>{/if}
    <span class="text"><span class="title">{track?.title ?? 'Nothing playing'}</span><span class="artist">{track?.artist ?? (ownSelected ? app.status?.message ?? '' : '')}</span></span>
  </button>
  <div class="centre">
    <div class="buttons">
      <button onclick={() => command('playlist', 'index', '-1')} aria-label="Previous"><Icon name="previous" /></button>
      <button class="play" onclick={() => command('pause', playing ? '1' : '0')} aria-label={playing ? 'Pause' : 'Play'}><Icon name={playing ? 'pause' : 'play'} size={20} /></button>
      <button onclick={() => command('playlist', 'index', '+1')} aria-label="Next"><Icon name="next" /></button>
    </div>
    <div class="seek-row">
      <span class="time">{formatTime(position)}</span>
      <div class="seek" role="slider" tabindex="0" aria-label="Seek" aria-valuenow={Math.round(position)} onclick={seek} onkeydown={(event) => { if (event.key === 'ArrowRight') command('time', Math.round(position + 10)); if (event.key === 'ArrowLeft') command('time', Math.max(0, Math.round(position - 10))) }}>
        <div class="fill" style:width={duration ? `${(position / duration) * 100}%` : '0'}></div>
      </div>
      <span class="time">{formatTime(duration)}</span>
    </div>
  </div>
  <div class="right">
    <div class="readout-slot"><Readout /></div>
    <Meters active={false} />
    <select class="players" value={app.selectedPlayer || app.ownPlayer} onchange={selectPlayer} aria-label="Player">
      {#each app.players as player (player.playerid)}<option value={player.playerid}>{player.name}{player.playerid === app.ownPlayer ? ' (this PC)' : ''}{player.connected ? '' : ' · offline'}</option>{/each}
    </select>
    {#if ownSelected && app.status?.device}<button class="device" onclick={() => sendToShell({ type: 'openSettings', tab: 'Audio' })} title={`Output: ${app.status.device}`}><Icon name="speaker" size={15} /><span class="device-name">{app.status.device}</span></button>{/if}
    <div class="volume">
      <input type="range" min="0" max="100" value={volume} onchange={(event) => command('mixer', 'volume', (event.target as HTMLInputElement).value)} aria-label="Volume" />
      <button class="volume-icon" onclick={() => volumeOpen = !volumeOpen} aria-label="Volume"><Icon name="volume" /></button>
      {#if volumeOpen}<div class="volume-pop"><input type="range" min="0" max="100" value={volume} onchange={(event) => command('mixer', 'volume', (event.target as HTMLInputElement).value)} aria-label="Volume" /></div>{/if}
    </div>
    {#if mini}<button class="pin" class:on={ui.pinned} onclick={() => { ui.pinned = !ui.pinned; sendToShell({ type: 'setTopmost', on: ui.pinned }) }} title="Always on top"><Icon name="pin" size={16} /></button>{/if}
  </div>
</footer>

<style>
  .transport { display: grid; grid-template-columns: minmax(180px, 1fr) minmax(260px, 1.4fr) minmax(180px, 1fr); align-items: center; gap: 16px; padding: 10px 16px; background: var(--glass); backdrop-filter: blur(var(--blur)) saturate(1.4); border-top: 1px solid var(--line); container-type: inline-size; }
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
  .right { display: flex; align-items: center; justify-content: flex-end; gap: 10px; min-width: 0; }
  .players { background: var(--panel); color: var(--fg); border: 1px solid var(--line); border-radius: 6px; padding: 3px 6px; max-width: 150px; font: inherit; font-size: 12px; }
  .device { background: none; border: 0; color: var(--fg-quiet); cursor: pointer; font-size: 12px; max-width: 140px; display: flex; align-items: center; gap: 5px; white-space: nowrap; overflow: hidden; }
  .device-name { overflow: hidden; text-overflow: ellipsis; }
  .volume { position: relative; display: flex; align-items: center; }
  .volume input { width: 90px; accent-color: var(--accent); }
  .volume-icon { display: none; background: none; border: 0; cursor: pointer; color: var(--fg); padding: 4px; }
  .volume-pop { position: absolute; bottom: 36px; right: 0; background: var(--panel-solid); border: 1px solid var(--line); border-radius: 8px; padding: 10px; box-shadow: 0 8px 24px var(--shadow); }
  .pin { background: none; border: 0; cursor: pointer; opacity: 0.5; color: var(--fg); padding: 4px; } .pin.on { opacity: 1; }
  /* Never drop transport controls: collapse the readout, player switcher, seek times, then the volume slider (SPEC.md › Transport) */
  @container (max-width: 1150px) { .readout-slot { display: none; } }
  @container (max-width: 900px) { .players, .device { display: none; } .transport { grid-template-columns: minmax(140px, 1fr) minmax(200px, 1.4fr) auto; } }
  @container (max-width: 640px) { .time { display: none; } }
  @container (max-width: 540px) { .volume > input { display: none; } .volume-icon { display: block; } .transport { grid-template-columns: 1fr auto auto; } .seek-row { display: none; } }
  /* Mini: Winamp-style strip */
  .mini { grid-template-columns: 1fr auto; grid-template-rows: auto auto; gap: 6px 10px; padding: 10px; height: 100%; box-sizing: border-box; border: 0; }
  .mini .centre { grid-column: 1 / 3; grid-row: 2; flex-direction: row; }
  .mini .seek-row { display: flex; } .mini .seek-row .time { display: none; }
  .mini .right .players, .mini .right .device, .mini .readout-slot, .mini .volume { display: none; }
  .mini .now img, .mini .no-art { width: 64px; height: 64px; }
  .mini .buttons { gap: 6px; } .mini .buttons button { width: 30px; height: 30px; } .mini .buttons .play { width: 34px; height: 34px; }
</style>
