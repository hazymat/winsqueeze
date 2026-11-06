<script lang="ts">
  import { fidelity } from '../lib/fidelity.svelte'
  import Icon from './Icon.svelte'
  // Above the volume: a rosette when playback is bit-perfect, "Scaled" below 100, "Not bit-perfect" otherwise (click either for why)
  const check = $derived(fidelity())
  let noteOpen = $state(false)
  $effect(() => { if (check.state === 'perfect' || !check.state) noteOpen = false })
</script>

{#if check.state === 'perfect'}
  <span class="badge perfect" title={`Bit-perfect: the decoded audio reaches the DAC untouched (native rate, exclusive output, volume 100, EQ off)${check.lossy ? '. The source itself is lossy.' : ''}`}><Icon name="verified" size={15} />Bit-perfect</span>
{:else if check.state}
  <button class="badge scaled" class:open={noteOpen} onclick={() => noteOpen = !noteOpen} title="Click to see why this isn't bit-perfect">{check.state === 'scaled' ? 'Scaled' : 'Not bit-perfect'}</button>
  {#if noteOpen}
    <div class="note" role="dialog">
      {#if check.state === 'scaled'}<strong>Volume scaled</strong>
        <p>WinSqueeze is turning the audio down digitally, so it isn't bit-perfect. For bit-perfect playback, set this volume to 100 and use Windows' volume (or your DAC's own knob) to control the level.</p>
        {#if check.reasons.length}<p class="quiet">Also not bit-perfect because {check.reasons.join(', ')}.</p>{/if}
      {:else}<strong>Not bit-perfect</strong><p>Because {check.reasons.join(', ')}.</p>{/if}
      <button class="close" onclick={() => noteOpen = false}>OK</button>
    </div>
  {/if}
{/if}

<style>
  .badge { position: absolute; left: 50%; bottom: calc(50% + 9px); transform: translateX(-50%); display: inline-flex; align-items: center; gap: 3px; height: 16px; padding: 0 6px; border-radius: 999px; font: inherit; font-size: 9.5px; font-weight: 700; letter-spacing: 0.06em; text-transform: uppercase; white-space: nowrap; line-height: 1; }
  .perfect { color: var(--accent); padding-left: 0; gap: 4px; }
  .scaled { background: none; border: 1px solid var(--line); color: var(--fg-quiet); cursor: pointer; }
  .scaled:hover, .scaled.open { color: var(--fg); background: var(--hover); }
  .note { position: absolute; right: 0; bottom: calc(50% + 32px); z-index: 50; width: 260px; padding: 12px 14px; background: var(--panel-solid); border: 1px solid var(--line); border-radius: 10px; box-shadow: 0 12px 32px var(--shadow); font-size: 12px; line-height: 1.45; color: var(--fg); }
  .note p { margin: 6px 0 0; } .note .quiet { color: var(--fg-quiet); }
  .close { margin-top: 10px; float: right; background: var(--accent-soft); color: var(--fg); border: 0; border-radius: 6px; padding: 4px 12px; font: inherit; font-size: 12px; cursor: pointer; }
</style>
