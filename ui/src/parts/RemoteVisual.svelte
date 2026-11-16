<script lang="ts">
  // In place of the meters while another player plays (SPEC.md › Meters): no player shares its levels, so this shows the real path instead.
  // This PC only exchanges small control messages with Lyrion (a blip out and back on each status check); the music goes from Lyrion to the player
  import { t } from '../lib/i18n'
  import { app, cycleMeterView } from '../state.svelte'
  let { size = 'large' }: { size?: 'small' | 'large' | 'full' } = $props()
  const player = $derived(app.players.find(player => player.playerid === app.selectedPlayer))
  const playerName = $derived(player?.name ?? ''), switchedOn = $derived(Number(player?.power ?? 1) === 1) // switched off: no control traffic shown either
  const playing = $derived(switchedOn && app.nowPlaying?.mode === 'play') // any source: library, radio, plugins
  const heading = $derived(!switchedOn ? t('{player} is switched off', { player: playerName }) : playing ? t('Playing on {player}', { player: playerName }) : app.nowPlaying?.mode === 'pause' ? t('Paused on {player}', { player: playerName }) : t('Nothing playing on {player}', { player: playerName }))
  let now = $state(performance.now()), blipFrom = $state(-9999)
  $effect(() => { let frame = 0; const step = (time: number) => { now = time; frame = requestAnimationFrame(step) }; frame = requestAnimationFrame(step); return () => cancelAnimationFrame(frame) })
  // A status reply sends a slow blip out and back (one at a time, so a reply mid-trip waits for the next); it fades in and out at the ends
  $effect(() => { app.receivedAt; if (switchedOn && performance.now() - blipFrom > blipMs) blipFrom = performance.now() })
  const blipMs = 2200, blipShare = $derived((now - blipFrom) / blipMs) // 0..0.5 out to Lyrion, 0.5..1 back
  const blipX = $derived(90 + 106 * (blipShare < 0.5 ? blipShare * 2 : 2 - blipShare * 2)), blipGlow = $derived(Math.sin(Math.PI * ((blipShare * 2) % 1)))
  // Music: four dots drift across in about 4 s, each brightening mid-way and fading at the ends
  const musicDots = $derived(Array.from({ length: 4 }, (unused, index) => (now / 4200 + index / 4) % 1).map(share => ({ dotX: 276 + 96 * share, glow: Math.sin(Math.PI * share) })))
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div class="remote {size}" oncontextmenu={(event) => { event.preventDefault(); event.stopPropagation(); cycleMeterView() }} title={heading}>
  <svg viewBox="0 0 470 140" aria-hidden="true">
    <rect x="1" y="1" width="468" height="138" rx="8" class="glass" />
    <text x="235" y="22" class="playing-on" class:quiet={!playing}>{heading.toUpperCase()}</text>
    <g transform="translate(26,52)" class="device"><rect x="0" y="0" width="50" height="32" rx="3" /><path d="M-7,38 h64" /><text x="25" y="60">{t('This PC')}</text></g>
    <path d="M90,68 L196,68" class="route control" />
    {#if blipShare < 1}<circle cx={blipX} cy="68" r="2.4" class="blip" opacity={0.25 + 0.75 * blipGlow} />{/if}
    <text x="143" y="88" class="caption">{t('control')}</text>
    <g transform="translate(206,40)" class="device"><rect x="0" y="0" width="58" height="16" rx="2" /><rect x="0" y="20" width="58" height="16" rx="2" /><rect x="0" y="40" width="58" height="16" rx="2" /><circle cx="48" cy="8" r="2" class="led" /><circle cx="48" cy="28" r="2" class="led" /><circle cx="48" cy="48" r="2" class="led" /><text x="29" y="76">Lyrion</text></g>
    <path d="M276,68 L372,68" class="route stream" class:on={playing} />
    {#if playing}{#each musicDots as dot}<circle cx={dot.dotX} cy="68" r="3.2" class="music" opacity={dot.glow} />{/each}{/if}
    <text x="324" y="88" class="caption">{t('music')}</text>
    <g transform="translate(386,36)" class="device speaker" class:on={playing}><rect x="0" y="0" width="40" height="58" rx="5" /><circle cx="20" cy="36" r="12" /><circle cx="20" cy="13" r="4.5" /><text x="20" y="80">{playerName}</text></g>
  </svg>
</div>

<style>
  /* Drawn in the accent colour (Settings › Appearance), so it suits every skin and colour */
  .remote { width: min(300px, 100cqw); } /* a little narrower than the VU pair, never wider than Now playing's details column */
  .remote.small { width: 240px; }
  .remote.full { width: min(60vw, 720px); }
  svg { display: block; width: 100%; height: auto; }
  .glass { fill: color-mix(in srgb, var(--accent) 5%, transparent); stroke: color-mix(in srgb, var(--accent) 28%, transparent); }
  .playing-on { font: 600 11px Inter, sans-serif; letter-spacing: 0.14em; fill: var(--accent); text-anchor: middle; } .playing-on.quiet { fill: var(--fg-quiet); }
  .device rect, .device circle, .device path { fill: none; stroke: color-mix(in srgb, var(--accent) 45%, var(--fg-quiet)); stroke-width: 2; }
  .device text { font: 600 12px Inter, sans-serif; fill: var(--fg); text-anchor: middle; stroke: none; }
  .device .led { fill: var(--accent); stroke: none; }
  .speaker.on rect, .speaker.on circle { stroke: var(--accent); }
  .route { fill: none; }
  .control { stroke: color-mix(in srgb, var(--accent) 35%, transparent); stroke-dasharray: 2 4; stroke-width: 1.2; }
  .stream { stroke: color-mix(in srgb, var(--fg-quiet) 30%, transparent); stroke-width: 2.5; } .stream.on { stroke: color-mix(in srgb, var(--accent) 28%, transparent); }
  .blip { fill: var(--accent); }
  .music { fill: var(--accent); filter: drop-shadow(0 0 4px var(--accent)); }
  .caption { font: 10px Inter, sans-serif; fill: var(--fg-quiet); text-anchor: middle; letter-spacing: 0.08em; }
</style>
