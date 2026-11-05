<script lang="ts">
  import { app, currentTrack, formatRate } from '../state.svelte'
  // Signal path: source format -> decoder -> output mode -> device rate, with the bit-perfect badge (SPEC.md › Playback and fidelity)
  const track = $derived(currentTrack())
  const ownPlayer = $derived(!app.selectedPlayer || app.selectedPlayer === app.ownPlayer)
  const codec = $derived(String(track?.type ?? '').toUpperCase().replace('FLC', 'FLAC').replace('ALC', 'ALAC'))
  const sourceRate = $derived(Number(track?.samplerate ?? 0))
  const bits = $derived(Number(track?.samplesize ?? 0))
  const outputRate = $derived(Number(app.status?.sampleRate ?? 0))
  const volume = $derived(Number(app.nowPlaying?.['mixer volume'] ?? 100))
  const lossless = $derived(['FLAC', 'ALAC', 'WAV', 'AIF', 'AIFF', 'PCM'].includes(codec))
  const bitPerfect = $derived(ownPlayer && app.status?.exclusive && lossless && sourceRate > 0 && sourceRate === outputRate && (volume >= 100 || app.status?.hardwareVolume) && !app.status?.eqActive) // hardware volume and a bypassed EQ keep the samples untouched
</script>

{#if track}
  <div class="readout" title={ownPlayer ? (bitPerfect ? 'Bit-perfect: lossless source, native rate, exclusive output, untouched samples' : app.status?.eqActive ? 'Not bit-perfect: the equaliser is on' : volume < 100 && !app.status?.hardwareVolume && app.status?.exclusive ? 'Not bit-perfect: player volume below 100 is applied digitally' : 'Signal path') : 'Another player'}>
    <span>{codec}{bits ? ` ${bits}` : ''}{sourceRate ? `/${formatRate(sourceRate).replace(' kHz', '')}` : ''}</span>
    {#if ownPlayer && app.status?.state === 'Running'}<span class="arrow">→</span><span>{app.status.exclusive ? 'WASAPI EXCL' : 'SHARED'}</span>{#if outputRate}<span class="arrow">→</span><span>{formatRate(outputRate)}</span>{/if}{/if}
    {#if bitPerfect}<span class="badge">BIT-PERFECT</span>{/if}
  </div>
{/if}

<style>
  .readout { display: flex; align-items: center; gap: 6px; font-family: var(--font-display); color: var(--readout); text-shadow: 0 0 6px var(--readout-glow); font-size: 12px; letter-spacing: 0.04em; white-space: nowrap; }
  .arrow { opacity: 0.6; }
  .badge { border: 1px solid var(--readout); border-radius: 8px; padding: 0 6px; font-size: 10px; }
</style>
