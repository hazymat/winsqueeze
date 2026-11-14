<script lang="ts">
  import { onShellMessage, sendToShell } from './bridge'
  import { app, meters, receiveLevels } from './state.svelte'
  import CustomVu, { darkRoom, faceHeights, type VuDesign } from './parts/CustomVu.svelte'
  // The VU Builder's preview (SPEC.md › VU Builder): the design being edited as a pair on black, moving with the music; with nothing playing the needles swing on their own so every change shows
  let design = $state<VuDesign>(darkRoom)
  onShellMessage('vuDesign', message => design = message.design)
  onShellMessage('levels', message => receiveLevels(message.vu, message.peak, message.bands, message.wide))
  onShellMessage('meterSettings', message => app.meterSettings = Object.assign({}, app.meterSettings, { referenceDbfs: message.referenceDbfs, autoReference: message.autoReference }))
  sendToShell({ type: 'ready' })
  let now = $state(performance.now())
  $effect(() => { let frame = requestAnimationFrame(function tick(time) { now = time; frame = requestAnimationFrame(tick) }); return () => cancelAnimationFrame(frame) })
  const live = $derived(now >= 0 && Date.now() - app.levels.at < 1200 && app.levels.vu.some(decibels => decibels > -60))
  const demo = (channel: number) => 0.62 + 0.13 * Math.sin(now / 1000 * (channel ? 1.1 : 1.45) + channel) + 0.05 * Math.sin(now / 1000 * (channel ? 3.9 : 4.6))
</script>

<main class="preview">
  {#each ['L', 'R'] as channel, channelIndex}<div class="meter" style:--aspect-scale={140 / (faceHeights[design.aspect] ?? 140)}><CustomVu {design} {channel} decibels={meters.vu[channelIndex] ?? -90} reference={meters.reference} reading={live ? null : demo(channelIndex)} peak={live ? meters.lamp[channelIndex] : Math.max(0, Math.min(1, (demo(channelIndex) - 0.76) * 25))} /></div>{/each}
</main>

<style>
  :global(html), :global(body) { margin: 0; height: 100%; background: #000; overflow: hidden; }
  .preview { position: fixed; inset: 0; background: #000; display: flex; align-items: center; justify-content: center; gap: 4vw; padding: 0 4vw; box-sizing: border-box; user-select: none; }
  .meter { flex: 1; max-width: calc((100vh - 40px) * 1.71 * var(--aspect-scale, 1)); }
  .meter :global(.custom-vu) { width: 100%; height: auto; }
</style>
