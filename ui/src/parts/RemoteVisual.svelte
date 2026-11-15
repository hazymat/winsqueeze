<script lang="ts">
  // In place of the meters while another player plays (SPEC.md › Meters): no player shares its levels, so this shows the real path instead.
  // This PC only exchanges small control messages with Lyrion (a blip out and back on each status check); the music goes from Lyrion to the player
  import { t } from '../lib/i18n'
  import { app, cycleMeterView } from '../state.svelte'
  let { size = 'large' }: { size?: 'small' | 'large' | 'full' } = $props()
  const playerName = $derived(app.players.find(player => player.playerid === app.selectedPlayer)?.name ?? '')
  const playing = $derived(app.nowPlaying?.mode === 'play') // any source: library, radio, plugins
  let now = $state(performance.now()), blipFrom = $state(-9999)
  $effect(() => { let frame = 0; const step = (time: number) => { now = time; frame = requestAnimationFrame(step) }; frame = requestAnimationFrame(step); return () => cancelAnimationFrame(frame) })
  $effect(() => { app.receivedAt; blipFrom = performance.now() }) // each status reply
  const blipShare = $derived((now - blipFrom) / 700) // 0..0.5 out to Lyrion, 0.5..1 back
  const blipX = $derived(90 + 106 * (blipShare < 0.5 ? blipShare * 2 : 2 - blipShare * 2))
  const musicDots = $derived(Array.from({ length: 6 }, (unused, index) => 276 + 96 * (((now / 1400) + index / 6) % 1)))
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div class="remote {size}" oncontextmenu={(event) => { event.preventDefault(); event.stopPropagation(); cycleMeterView() }} title={t('Playing on {player}', { player: playerName })}>
  <svg viewBox="0 0 470 140" aria-hidden="true">
    <rect x="1" y="1" width="468" height="138" rx="8" class="glass" />
    <text x="235" y="22" class="playing-on">{t('Playing on {player}', { player: playerName }).toUpperCase()}</text>
    <g transform="translate(26,52)" class="device"><rect x="0" y="0" width="50" height="32" rx="3" /><path d="M-7,38 h64" /><text x="25" y="60">{t('This PC')}</text></g>
    <path d="M90,68 L196,68" class="route control" />
    {#if blipShare < 1}<circle cx={blipX} cy="68" r="2.2" class="blip" />{/if}
    <text x="143" y="88" class="caption">{t('control')}</text>
    <g transform="translate(206,40)" class="device"><rect x="0" y="0" width="58" height="16" rx="2" /><rect x="0" y="20" width="58" height="16" rx="2" /><rect x="0" y="40" width="58" height="16" rx="2" /><circle cx="48" cy="8" r="2" class="led" /><circle cx="48" cy="28" r="2" class="led" /><circle cx="48" cy="48" r="2" class="led" /><text x="29" y="76">Lyrion</text></g>
    <path d="M276,68 L372,68" class="route stream" class:on={playing} />
    {#if playing}{#each musicDots as dotX}<circle cx={dotX} cy="68" r="3.2" class="music" />{/each}{/if}
    <text x="324" y="88" class="caption">{t('music')}</text>
    <g transform="translate(386,36)" class="device speaker" class:on={playing}><rect x="0" y="0" width="40" height="58" rx="5" /><circle cx="20" cy="36" r="12" /><circle cx="20" cy="13" r="4.5" /><text x="20" y="80">{playerName}</text></g>
  </svg>
</div>

<style>
  .remote { width: min(384px, 100cqw); } /* as wide as the VU pair, never wider than Now playing's details column */
  .remote.small { width: 300px; }
  .remote.full { width: min(92vw, 1100px); }
  svg { display: block; width: 100%; height: auto; }
  .glass { fill: rgba(10, 18, 20, 0.85); stroke: rgba(127, 227, 210, 0.25); }
  .playing-on { font: 600 11px Inter, sans-serif; letter-spacing: 0.14em; fill: #7fe3d2; text-anchor: middle; animation: pulse 2.4s ease-in-out infinite; }
  .device rect, .device circle, .device path { fill: none; stroke: #9fb6b2; stroke-width: 2; }
  .device text { font: 600 12px Inter, sans-serif; fill: #c9d6d4; text-anchor: middle; }
  .device .led { fill: #7fe3d2; stroke: none; }
  .speaker.on rect, .speaker.on circle { stroke: #7fe3d2; }
  .route { fill: none; }
  .control { stroke: rgba(159, 182, 178, 0.35); stroke-dasharray: 2 4; stroke-width: 1.2; }
  .stream { stroke: rgba(159, 182, 178, 0.2); stroke-width: 2.5; } .stream.on { stroke: rgba(127, 227, 210, 0.3); }
  .blip { fill: #c9d6d4; }
  .music { fill: #7fe3d2; filter: drop-shadow(0 0 4px #7fe3d2); }
  .caption { font: 10px Inter, sans-serif; fill: rgba(255, 255, 255, 0.4); text-anchor: middle; letter-spacing: 0.08em; }
  @keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.35; } }
</style>
