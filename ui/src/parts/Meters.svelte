<script lang="ts">
  import { ui, writeSetting } from '../state.svelte'
  // Squeezelite on Windows has no level output yet (SPEC.md › Risks), so the meters sit in their muted idle state
  let { size = 'small', active = false }: { size?: 'small' | 'large'; active?: boolean } = $props()
  const toggle = () => { ui.meterStyle = ui.meterStyle === 'vu' ? 'oled' : 'vu'; writeSetting('meterStyle', ui.meterStyle) }
  const needleAngle = -48
</script>

<button class="meters {size}" class:idle={!active} onclick={toggle} title={active ? 'Click to switch meter style' : 'Level meters (waiting for level data from Squeezelite). Click to switch style.'}>
  {#if ui.meterStyle === 'vu'}
    {#each ['L', 'R'] as channel}
      <svg viewBox="0 0 100 60" class="vu" aria-hidden="true">
        <rect x="1" y="1" width="98" height="58" rx="6" class="face" />
        <path d="M14 46 A40 40 0 0 1 86 46" class="scale" />
        <path d="M66 22 A40 40 0 0 1 86 46" class="red" />
        <line x1="50" y1="54" x2="50" y2="14" class="needle" transform="rotate({needleAngle} 50 54)" />
        <text x="50" y="52" class="label">{channel}</text>
      </svg>
    {/each}
  {:else}
    <div class="oled">
      {#each ['L', 'R'] as channel}<div class="bar"><span class="ch">{channel}</span>{#each Array(16) as _, segment}<span class="segment" class:hot={segment >= 13}></span>{/each}</div>{/each}
    </div>
  {/if}
</button>

<style>
  .meters { display: flex; gap: 4px; background: none; border: 0; padding: 0; cursor: pointer; align-items: center; }
  .small .vu { width: 52px; height: 31px; } .large .vu { width: 150px; height: 90px; }
  .face { fill: var(--meter-face); stroke: var(--line); }
  .scale { fill: none; stroke: var(--meter-ink); stroke-width: 2; }
  .red { fill: none; stroke: #ff5a46; stroke-width: 3; }
  .needle { stroke: var(--meter-needle); stroke-width: 1.6; transition: transform 300ms ease-out; }
  .label { fill: var(--meter-ink); font-size: 9px; text-anchor: middle; font-family: var(--font-display); }
  .idle .face { fill: var(--meter-face-idle); } .idle .needle { opacity: 0.45; }
  .oled { display: flex; flex-direction: column; gap: 3px; background: #050607; padding: 4px 6px; border-radius: 4px; }
  .bar { display: flex; gap: 2px; align-items: center; }
  .ch { font-family: var(--font-display); font-size: 8px; color: var(--readout); width: 8px; }
  .segment { width: 4px; height: 7px; background: rgba(94, 231, 255, 0.12); border-radius: 1px; }
  .segment.hot { background: rgba(255, 90, 70, 0.15); }
  .large .segment { width: 9px; height: 16px; } .large .ch { font-size: 12px; width: 14px; }
</style>
