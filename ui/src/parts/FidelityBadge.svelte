<script lang="ts">
  import { fidelity } from '../lib/fidelity.svelte'
  import Icon from './Icon.svelte'
  import { app, currentTrack, formatRate } from '../state.svelte'
  // Above the volume: the format, then a rosette when playback is bit-perfect, "Scaled" below 100, "Not bit-perfect" otherwise; click any of them for the details
  const check = $derived(fidelity())
  let noteOpen = $state(false), infoOpen = $state(false)
  // The format opens what we know about the file and how it's being played
  const track = $derived(currentTrack())
  const fileRows = $derived.by(() => {
    if (!track) return [] as [string, string][]
    const status = app.status ?? {}, sourceRate = Number(track.samplerate ?? 0), outputRate = Number(status.sampleRate ?? 0)
    const bitrate = String(track.bitrate ?? '').replace(/kbps/i, ' kbps').trim(), sizeBytes = Number(track.filesize ?? 0)
    const rows: [string, string][] = [['Format', check.format + (check.lossy ? ' (lossy)' : ' (lossless)')], ['Bitrate', bitrate], ['Sample rate', sourceRate ? formatRate(sourceRate) : ''], ['Bit depth', track.samplesize ? `${track.samplesize}-bit` : ''], ['File size', sizeBytes ? `${(sizeBytes / 1048576).toFixed(1)} MB` : ''], ['ReplayGain', track.replay_gain ? `${track.replay_gain} dB` : ''],
      ['Signal path', `${check.format}${sourceRate ? ' ' + formatRate(sourceRate) : ''} → ${status.exclusive ? 'WASAPI exclusive' : 'Windows mixer'}${outputRate ? ' → ' + formatRate(outputRate) : ''}`], ['Output', status.exclusive ? 'WASAPI exclusive' : 'Shared (Windows mixer)'], ['Device', status.device ?? ''], ['Device rate', outputRate ? formatRate(outputRate) + (sourceRate && outputRate !== sourceRate ? ' (resampled)' : '') : ''], ['Equaliser', status.eqActive ? 'On' : 'Off (bypassed)'], ['Volume', String(Math.abs(Number(app.nowPlaying?.['mixer volume'] ?? 0)))]]
    return rows.filter(row => row[1])
  })
  let shownState = ''
  $effect(() => { if (check.state !== shownState) { shownState = check.state; noteOpen = false; infoOpen = false } })
</script>
<svelte:window onkeydown={(event) => { if (event.key === 'Escape' && (noteOpen || infoOpen)) { event.preventDefault(); noteOpen = false; infoOpen = false } }} />

{#if check.state}
  <div class="fidelity">
  {#if check.format}<button class="format" class:open={infoOpen} onclick={() => { infoOpen = !infoOpen; noteOpen = false }} title="File and playback details">{check.format}</button>{/if}
  {#if infoOpen}
    <div class="note info" role="dialog">
      <strong>{track?.title ?? 'Now playing'}</strong>
      <dl>{#each fileRows as [label, value]}<dt>{label}</dt><dd>{value}</dd>{/each}</dl>
      <button class="close" onclick={() => infoOpen = false}>OK</button>
    </div>
  {/if}
  <button class="badge" class:perfect={check.state === 'perfect'} class:scaled={check.state !== 'perfect'} class:open={noteOpen} onclick={() => { noteOpen = !noteOpen; infoOpen = false }} title="Click for details">{#if check.state === 'perfect'}<Icon name="verified" size={15} />Bit-perfect{:else}{check.state === 'scaled' ? 'Scaled' : 'Not bit-perfect'}{/if}</button>
  {#if noteOpen}
    <div class="note" role="dialog">
      {#if check.state === 'perfect'}<strong>Bit-perfect</strong>
        <p>The decoded audio reaches your DAC untouched: native sample rate, exclusive output, volume at 100 and the EQ off.</p>
        {#if check.lossy}<p class="quiet">The source file is lossy (e.g. MP3), so it's a perfect copy of the decoded file, not of the original recording.</p>{/if}
      {:else if check.state === 'scaled'}<strong>Volume scaled</strong>
        <p>WinSqueeze is turning the audio down digitally, so it isn't bit-perfect. For bit-perfect playback, set this volume to 100 and use Windows' volume (or your DAC's own knob) to control the level.</p>
        {#if check.reasons.length}<p class="quiet">Also not bit-perfect because {check.reasons.join(', ')}.</p>{/if}
      {:else}<strong>Not bit-perfect</strong><p>Because {check.reasons.join(', ')}.</p>{/if}
      <button class="close" onclick={() => noteOpen = false}>OK</button>
    </div>
  {/if}
  </div>
{/if}

<style>
  .fidelity { position: relative; display: flex; flex-direction: column; align-items: flex-end; gap: 1px; }
  .badge { display: inline-flex; align-items: center; gap: 3px; height: 16px; padding: 0 6px; border-radius: 999px; font: inherit; font-size: 9.5px; font-weight: 700; letter-spacing: 0.06em; text-transform: uppercase; white-space: nowrap; line-height: 1; }
  .perfect { background: none; border: 0; color: var(--accent); padding: 0; margin-right: -0.06em; gap: 4px; cursor: pointer; } .perfect:hover, .perfect.open { filter: brightness(1.2); }
  .format { background: none; border: 0; padding: 0; cursor: pointer; font-family: inherit; color: var(--fg-quiet); font-size: 10px; font-weight: 700; letter-spacing: 0.06em; line-height: 1; margin-right: -0.06em; } /* the last letter's spacing would push it left of the line below */
  .scaled { background: none; border: 0; padding: 0; margin-right: -0.06em; color: var(--fg-quiet); cursor: pointer; } /* plain text like Bit-perfect, so its right edge lines up with the format */
  .scaled:hover, .scaled.open { color: var(--fg); text-decoration: underline; text-underline-offset: 2px; }
  .note { position: absolute; right: 0; bottom: calc(100% + 8px); z-index: 50; width: 260px; padding: 12px 14px; background: var(--panel-solid); border: 1px solid var(--line); border-radius: 10px; box-shadow: 0 12px 32px var(--shadow); font-size: 12px; line-height: 1.45; color: var(--fg); }
  .note p { margin: 6px 0 0; } .note .quiet { color: var(--fg-quiet); }
  .close { margin-top: 10px; float: right; background: var(--accent-soft); color: var(--fg); border: 0; border-radius: 6px; padding: 4px 12px; font: inherit; font-size: 12px; cursor: pointer; }
  .format:hover, .format.open { color: var(--fg); }
  .info { width: 300px; }
  dl { display: grid; grid-template-columns: auto 1fr; gap: 3px 12px; margin: 8px 0 0; } dt { color: var(--fg-quiet); } dd { margin: 0; overflow-wrap: anywhere; }
</style>
