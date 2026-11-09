<script lang="ts">
  import { t } from '../lib/i18n'
  import { app, currentTrack, formatRate } from '../state.svelte'
  import { fidelity } from '../lib/fidelity.svelte'
  // Signal path: source format -> decoder -> output mode -> device rate (the bit-perfect badge sits above the volume)
  const track = $derived(currentTrack())
  const ownPlayer = $derived(!app.selectedPlayer || app.selectedPlayer === app.ownPlayer)
  const codec = $derived(String(track?.type ?? '').toUpperCase().replace('FLC', 'FLAC').replace('ALC', 'ALAC'))
  const sourceRate = $derived(Number(track?.samplerate ?? 0))
  const bits = $derived(Number(track?.samplesize ?? 0))
  const outputRate = $derived(Number(app.status?.sampleRate ?? 0))
  const check = $derived(fidelity())
</script>

{#if track}
  <div class="readout" title={ownPlayer ? (check.state === 'perfect' ? t('Bit-perfect') : check.reasons.length ? t('Not bit-perfect:') + '\n• ' + check.reasons.join('\n• ') : t('Signal path')) : t('Another player')}>
    <span>{codec}{bits ? ` ${bits}` : ''}{sourceRate ? `/${formatRate(sourceRate).replace(' kHz', '')}` : ''}</span>
    {#if ownPlayer && app.status?.state === 'Running'}<span class="arrow">→</span><span>{app.status.exclusive ? 'WASAPI EXCL' : 'SHARED'}</span>{#if outputRate}<span class="arrow">→</span><span>{formatRate(outputRate)}</span>{/if}{/if}
  </div>
{/if}

<style>
  .readout { display: flex; align-items: center; gap: 6px; font-family: var(--font-display); color: var(--readout); text-shadow: 0 0 6px var(--readout-glow); font-size: 12px; letter-spacing: 0.04em; white-space: nowrap; }
  .arrow { opacity: 0.6; }
</style>
