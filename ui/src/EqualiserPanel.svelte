<script lang="ts">
  import { sendToShell, onShellMessage } from './bridge'
  import Analyser from './parts/Analyser.svelte'
  // The graphic equaliser window (SPEC.md › Equaliser): a black brushed hi-fi panel with gold legends, a fader bank per channel,
  // and a recessed analyser between them. Settings travel to the shell as the whole EQ object (PascalCase, as stored).
  const frequencies: Record<number, number[]> = { 10: [31.5, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000], 7: [63, 160, 400, 1000, 2500, 6300, 16000], 3: [100, 1000, 8000] }
  const range = 10 // ±dB on the faders
  let eq = $state<any>(null)
  let supported = $state(true), preampDb = $state(0)
  let analyser = $state({ left: Array(10).fill(0), right: Array(10).fill(0) })
  let analyserTargets = { left: Array(10).fill(0), right: Array(10).fill(0) }
  let hover = $state<{ side: string; band: number } | null>(null)
  let shiftHeld = $state(false)

  onShellMessage('eq', message => { eq = message.eq; supported = message.supported; preampDb = message.preampDb })
  onShellMessage('eqPreamp', message => preampDb = message.preampDb)
  // Octave levels in dBFS; a +3 dB/octave tilt (pivot 1 kHz) makes typical music read evenly. Scale: 3..30 over -33..-6 dBFS
  const toLevel = (decibels: number, band: number) => Math.max(0, Math.min(1, (decibels + 3 * Math.log2(frequencies[10][band] / 1000) + 36) / 30))
  onShellMessage('eqLevels', message => analyserTargets = { left: message.left.map(toLevel), right: message.right.map(toLevel) })
  sendToShell({ type: 'ready' })
  // Instant rise, then a fall set by DECAY
  $effect(() => {
    let frame = 0, last = performance.now()
    const step = (now: number) => {
      const fall = 1 - Math.exp(-(now - last) / (eq?.Decay === 'slow' ? 600 : 140)); last = now
      const ease = (current: number[], target: number[]) => current.map((value, index) => target[index] >= value ? target[index] : value + (target[index] - value) * fall)
      analyser = { left: ease(analyser.left, analyserTargets.left), right: ease(analyser.right, analyserTargets.right) }
      frame = requestAnimationFrame(step)
    }
    frame = requestAnimationFrame(step)
    return () => cancelAnimationFrame(frame)
  })

  const bandCount = $derived(eq ? (frequencies[eq.BandCount] ? eq.BandCount : 10) : 10)
  const keyFor = (side: string) => `${bandCount}${eq.Linked ? 'L' : side}`
  const gainsFor = (side: string): number[] => { const stored = eq?.Gains?.[keyFor(side)]; return stored && stored.length === bandCount ? stored : Array(bandCount).fill(0) }
  const formatFrequency = (frequency: number) => frequency >= 1000 ? `${frequency / 1000}k` : String(frequency)
  const shortFrequency = (frequency: number) => frequency === 31.5 ? '31' : formatFrequency(frequency) // legends under narrow columns
  const bandLabel = (band: number) => bandCount === 3 ? ['BASS', 'MID', 'TREBLE'][band] : shortFrequency(frequencies[bandCount][band])
  const formatGain = (gain: number) => `${gain > 0 ? '+' : ''}${gain.toFixed(1)} dB`

  function send(commit = false) { sendToShell({ type: 'eqSet', eq: $state.snapshot(eq), commit }) }
  function setGains(side: string, gains: number[]) { const nextGains = Object.assign({}, eq.Gains); nextGains[keyFor(side)] = gains; eq.Gains = nextGains }
  function change(field: string, value: any) {
    if (field === 'Linked' && !value) { const shared = gainsFor('L'); eq.Linked = false; setGains('L', shared.slice()); setGains('R', shared.slice()) } // unlink starting from the shared curve
    else eq[field] = value
    send(true)
  }
  function copy(from: string, to: string) { const source = gainsFor(from); setGains(to, source.slice()); send(true) }
  function reset() { setGains('L', Array(bandCount).fill(0)); if (!eq.Linked) setGains('R', Array(bandCount).fill(0)); send(true) }

  // ---- Faders: DRAW sweeps across them (each jumps to the pointer), SINGLE moves only the one you grabbed; Shift swaps the mode ----
  let pressed: { side: string; band: number; lastBand: number; lastGain: number; draw: boolean } | null = null
  function locate(event: PointerEvent, bank: HTMLElement) {
    const track = bank.querySelector('.tracks') as HTMLElement, box = track.getBoundingClientRect()
    const band = Math.max(0, Math.min(bandCount - 1, Math.floor((event.clientX - box.left) / box.width * bandCount)))
    const gain = Math.round(Math.max(-range, Math.min(range, range - (event.clientY - box.top) / box.height * 2 * range)) * 2) / 2
    return { band, gain }
  }
  function setBand(side: string, band: number, gain: number) { const gains = gainsFor(side).slice(); gains[band] = gain; setGains(side, gains) }
  function pointerDown(event: PointerEvent, side: string) {
    if (event.button !== 0) return
    const bank = event.currentTarget as HTMLElement; bank.setPointerCapture(event.pointerId)
    const { band, gain } = locate(event, bank)
    pressed = { side, band, lastBand: band, lastGain: gain, draw: (eq.EditMode !== 'single') !== event.shiftKey }
    setBand(side, band, gain); send()
  }
  function pointerMove(event: PointerEvent, side: string) {
    const bank = event.currentTarget as HTMLElement
    const { band, gain } = locate(event, bank)
    shiftHeld = event.shiftKey
    if (!pressed) { hover = { side, band }; return }
    if (pressed.draw) {
      // Fill any faders skipped by a fast sweep, along the line between the last point and this one
      const gains = gainsFor(side).slice(), direction = Math.sign(band - pressed.lastBand)
      for (let between = pressed.lastBand; between !== band; between += direction) gains[between] = Math.round((pressed.lastGain + (gain - pressed.lastGain) * (between - pressed.lastBand) / (band - pressed.lastBand)) * 2) / 2
      gains[band] = gain; setGains(side, gains)
      pressed.lastBand = band; pressed.lastGain = gain; hover = { side, band }
    } else { setBand(side, pressed.band, gain); hover = { side, band: pressed.band } }
    send()
  }
  function pointerUp() { if (pressed) { pressed = null; send(true) } }
  function wheel(event: WheelEvent, side: string) { const { band } = locate(event as unknown as PointerEvent, event.currentTarget as HTMLElement); const gains = gainsFor(side); setBand(side, band, Math.max(-range, Math.min(range, gains[band] + (event.deltaY < 0 ? 0.5 : -0.5)))); send(true) }
  function doubleClick(event: MouseEvent, side: string) { const { band } = locate(event as unknown as PointerEvent, event.currentTarget as HTMLElement); setBand(side, band, 0); send(true) }
  const knobTop = (gain: number) => `${(range - gain) / (2 * range) * 100}%`
  const scaleMarks = [10, 8, 6, 4, 2, 0, 2, 4, 6, 8, 10]

  function dragWindow(event: MouseEvent) { if (event.button === 0 && !(event.target as HTMLElement).closest('button, .bank')) sendToShell({ type: 'dragWindow' }) }
  const onKey = (event: KeyboardEvent) => { shiftHeld = event.shiftKey; if (event.key === 'Escape') sendToShell({ type: 'close' }) }
</script>

<svelte:window onkeydown={onKey} onkeyup={(event) => shiftHeld = event.shiftKey} />

{#snippet bank(side: string)}
  {@const gains = gainsFor(side)}
  <div class="bank-wrap" class:left={side === 'L'}>
    {#if side === 'R'}<div class="scale">{#each scaleMarks as mark, index}<span>{index === 0 ? '+' : index === 10 ? '−' : ''}{mark}</span>{/each}</div>{/if}
    <div class="bank" role="group" aria-label={side === 'L' ? 'Left channel' : 'Right channel'} class:dimmed={eq.Linked && side === 'R'}
      onpointerdown={(event) => pointerDown(event, side)} onpointermove={(event) => pointerMove(event, side)} onpointerup={pointerUp} onpointercancel={pointerUp}
      onpointerleave={() => { if (!pressed) hover = null }} onwheel={(event) => wheel(event, side)} ondblclick={(event) => doubleClick(event, side)}>
      <div class="tracks">
        {#each scaleMarks as mark, index}<div class="tick" class:zero={index === 5} style:top="{index * 10}%"></div>{/each}
        {#each gains as gain, band}
          <div class="fader" class:hovered={hover && (hover.side === side || eq.Linked) && hover.band === band}>
            <div class="slot"></div>
            <div class="knob" style:top={knobTop(gain)}></div>
          </div>
        {/each}
      </div>
      <div class="labels">{#each gains as unused, band}<span>{bandLabel(band)}</span>{/each}</div>
    </div>
    {#if side === 'L'}<div class="scale">{#each scaleMarks as mark, index}<span>{index === 0 ? '+' : index === 10 ? '−' : ''}{mark}</span>{/each}</div>{/if}
  </div>
{/snippet}

{#snippet pushButton(label: string, options: [string, any][], field: string)}
  {@const current = eq[field]}
  <button class="push" onclick={() => { const index = options.findIndex(([, value]) => value === current); change(field, options[(index + 1) % options.length][1]) }}>
    <span class="push-label">{label}</span>
    <span class="push-options">{#each options as [optionLabel, value], index}{#if index}<i>/</i>{/if}<b class:active={value === current}>{optionLabel}</b>{/each}</span>
    <span class="cap"></span>
  </button>
{/snippet}

<div class="panel" role="presentation" onmousedown={dragWindow}>
  <div class="sheen"></div>
  {#if eq}
    <header>
      <span class="brand">WinSqueeze</span>
      <span class="model">GRAPHIC EQUALIZER · SPECTRUM ANALYZER</span>
      <span class="readout">{hover ? `${bandCount === 3 ? bandLabel(hover.band) : formatFrequency(frequencies[bandCount][hover.band]) + ' Hz'} · ${eq.Linked ? 'L+R' : hover.side} ${formatGain(gainsFor(hover.side)[hover.band])}` : eq.Enabled ? `PREAMP ${preampDb.toFixed(1)} dB · ${eq.Character === 'precise' ? 'PRECISE' : 'VINTAGE'}` : 'BYPASS · BIT-PERFECT'}</span>
      <button class="close" onclick={() => sendToShell({ type: 'close' })} aria-label="Close">×</button>
    </header>
    <div class="body">
      <div class="power-column">
        <span class="legend">EQ</span>
        <button class="power" class:on={eq.Enabled} onclick={() => change('Enabled', !eq.Enabled)} aria-pressed={eq.Enabled} title="Equaliser on, or bypassed (bit-perfect)"></button>
        <span class="led" class:on={eq.Enabled}></span>
        <span class="legend small">{eq.Enabled ? 'ON' : 'BYPASS'}</span>
      </div>
      {@render bank('L')}
      <div class="centre">
        <div class="analyser-slot"><Analyser levels={analyser.left.map((level, index) => eq.Display === 'split' ? level : (level + analyser.right[index]) / 2)} levelsRight={eq.Display === 'split' ? analyser.right : null} labels={frequencies[10].map(shortFrequency)} scale={['30', '24', '18', '12', '6', '3']} /></div>
        <div class="legend title">GRAPHIC EQUALIZER</div>
        <div class="legend arrows"><span>◄ LEFT</span><span class="rule"></span><span>FREQUENCY (Hz)</span><span class="rule"></span><span>RIGHT ►</span></div>
      </div>
      {@render bank('R')}
      <div class="cluster">
        {@render pushButton('BANDS', [['10', 10], ['7', 7], ['3', 3]], 'BandCount')}
        {@render pushButton('CHARACTER', [['VINTAGE', 'vintage'], ['PRECISE', 'precise']], 'Character')}
        {@render pushButton('STEREO', [['LINK', true], ['L/R', false]], 'Linked')}
        {@render pushButton('EDIT', [['DRAW', 'draw'], ['SINGLE', 'single']], 'EditMode')}
        {@render pushButton('DISPLAY', [['L+R', 'sum'], ['L/R', 'split']], 'Display')}
        {@render pushButton('DECAY TIME', [['FAST', 'fast'], ['SLOW', 'slow']], 'Decay')}
        <button class="push" disabled={eq.Linked} onclick={() => copy('L', 'R')}><span class="push-label">COPY</span><span class="push-options"><b>L → R</b></span><span class="cap"></span></button>
        <button class="push" disabled={eq.Linked} onclick={() => copy('R', 'L')}><span class="push-label">COPY</span><span class="push-options"><b>R → L</b></span><span class="cap"></span></button>
        <button class="push wide" onclick={reset}><span class="push-label">RESET</span><span class="push-options"><b>FLAT</b></span><span class="cap"></span></button>
        <div class="hint">{shiftHeld ? (eq.EditMode === 'single' ? 'Shift: drawing' : 'Shift: single fader') : 'Shift swaps edit mode · double-click = 0 dB · scroll = ±0.5'}</div>
      </div>
    </div>
    {#if !supported}<div class="unsupported">The equaliser needs WinSqueeze's own player. It will work once the bundled Squeezelite is running.</div>{/if}
  {/if}
</div>

<style>
  :global(html), :global(body) { margin: 0; height: 100%; background: #0b0b0c; overflow: hidden; user-select: none; }
  .panel { --gold: #c9a85c; --gold-dim: rgba(201, 168, 92, 0.55); position: relative; height: 100vh; box-sizing: border-box; padding: 10px 16px 14px; display: flex; flex-direction: column; color: var(--gold); font-family: Arial, "Segoe UI", sans-serif; overflow: hidden;
    /* black brushed aluminium */
    background: repeating-linear-gradient(90deg, rgba(255, 255, 255, 0.018) 0 1px, transparent 1px 2px, rgba(0, 0, 0, 0.05) 2px 3px), linear-gradient(180deg, #1c1c1e 0%, #111112 55%, #0c0c0d 100%); }
  /* smoked acrylic front: a soft diagonal reflection over everything */
  .sheen { position: absolute; inset: 0; pointer-events: none; z-index: 5; background: linear-gradient(165deg, rgba(255, 255, 255, 0.07) 0%, rgba(255, 255, 255, 0.025) 30%, transparent 31%, transparent 70%, rgba(255, 255, 255, 0.02) 100%); box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.08), inset 0 -2px 6px rgba(0, 0, 0, 0.6); }
  header { display: flex; align-items: baseline; gap: 16px; margin-bottom: 8px; }
  .brand { font-family: Georgia, "Times New Roman", serif; font-weight: 700; font-size: 17px; letter-spacing: 1.5px; color: var(--gold); text-shadow: 0 1px 0 rgba(0, 0, 0, 0.8); }
  .model { font-size: 9.5px; letter-spacing: 1.4px; }
  .readout { margin-left: auto; font-size: 9px; letter-spacing: 1.2px; color: #5fe6ee; text-shadow: 0 0 4px rgba(95, 230, 238, 0.5); }
  .close { z-index: 6; background: none; border: 0; color: var(--gold-dim); font-size: 18px; cursor: pointer; padding: 0 2px; line-height: 1; } .close:hover { color: var(--gold); }
  .body { flex: 1; min-height: 0; display: flex; gap: 14px; align-items: stretch; }
  .legend { font-size: 9px; letter-spacing: 1.2px; text-align: center; } .legend.small { font-size: 8px; }
  .power-column { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8px; width: 64px; flex-shrink: 0; }
  .power { width: 46px; height: 46px; border-radius: 3px; cursor: pointer; border: 1px solid #000; background: linear-gradient(160deg, #2a2a2d, #141416 60%, #0e0e10); box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.09), inset 0 -2px 3px rgba(0, 0, 0, 0.7), 0 2px 4px rgba(0, 0, 0, 0.6); transition: transform 60ms; }
  .power:active { transform: translateY(1px); }
  .power.on { box-shadow: inset 0 2px 4px rgba(0, 0, 0, 0.8), inset 0 -1px 0 rgba(255, 255, 255, 0.05); }
  .led { width: 6px; height: 6px; border-radius: 50%; background: #2a1810; box-shadow: inset 0 0 1px #000; } .led.on { background: #ff6a3d; box-shadow: 0 0 6px #ff6a3d; }
  .bank-wrap { flex: 1.35; min-width: 0; display: flex; gap: 6px; }
  .bank { flex: 1; min-width: 0; display: flex; flex-direction: column; cursor: crosshair; touch-action: none; }
  .bank.dimmed .knob { opacity: 0.85; }
  .tracks { flex: 1; position: relative; display: flex; margin: 6px 0; }
  .tick { position: absolute; left: 0; right: 0; height: 1px; background: rgba(201, 168, 92, 0.28); } .tick.zero { background: rgba(201, 168, 92, 0.5); }
  .fader { flex: 1; position: relative; display: flex; justify-content: center; border-radius: 3px; }
  .fader.hovered { background: linear-gradient(180deg, rgba(201, 168, 92, 0.05), rgba(201, 168, 92, 0.12), rgba(201, 168, 92, 0.05)); box-shadow: 0 0 0 1px rgba(201, 168, 92, 0.18); }
  .slot { position: absolute; top: -4px; bottom: -4px; width: 3px; border-radius: 2px; background: #000; box-shadow: inset 0 1px 1px rgba(0, 0, 0, 0.9), 0 0 0 1px rgba(255, 255, 255, 0.04); }
  .knob { position: absolute; width: min(14px, 70%); height: 22px; margin-top: -11px; border-radius: 2px; background: linear-gradient(180deg, #303033 0%, #1a1a1c 45%, #0f0f10 55%, #232325 100%); box-shadow: 0 2px 3px rgba(0, 0, 0, 0.8), inset 0 1px 0 rgba(255, 255, 255, 0.12); }
  .knob::after { content: ""; position: absolute; left: 2px; right: 2px; top: 50%; height: 1px; background: rgba(255, 255, 255, 0.55); }
  .labels { display: flex; font-size: 8.5px; letter-spacing: 0.3px; } .labels span { flex: 1; text-align: center; white-space: nowrap; overflow: hidden; }
  .scale { display: flex; flex-direction: column; justify-content: space-between; font-size: 8px; margin: 1px 0 16px; text-align: right; min-width: 16px; line-height: 1; }
  .centre { flex: 1.2; min-width: 200px; max-width: 380px; display: flex; flex-direction: column; gap: 5px; }
  .analyser-slot { flex: 1; min-height: 0; display: flex; padding: 5px; border-radius: 6px; background: #050506; box-shadow: inset 0 2px 5px rgba(0, 0, 0, 0.9), 0 1px 0 rgba(255, 255, 255, 0.06); }
  .analyser-slot :global(.analyser) { flex: 1; } .analyser-slot :global(.legend span) { font-size: 7.5px; letter-spacing: 0; }
  .title { letter-spacing: 2px; font-size: 10px; }
  .arrows { display: flex; align-items: center; gap: 6px; font-size: 8px; white-space: nowrap; } .rule { flex: 1; height: 1px; background: var(--gold-dim); }
  .cluster { width: 176px; flex-shrink: 0; display: grid; grid-template-columns: 1fr 1fr; gap: 7px 8px; align-content: center; }
  .push { position: relative; display: flex; flex-direction: column; align-items: center; gap: 1px; background: none; border: 0; padding: 0 0 15px; color: var(--gold); cursor: pointer; font-family: inherit; }
  .push.wide { grid-column: 1 / 3; }
  .push:disabled { opacity: 0.35; cursor: default; }
  .push-label { font-size: 8.5px; letter-spacing: 1px; }
  .push-options { font-size: 7.5px; letter-spacing: 0.5px; color: var(--gold-dim); } .push-options i { font-style: normal; margin: 0 2px; } .push-options b { font-weight: 400; } .push-options b.active { color: #f2d48a; text-shadow: 0 0 4px rgba(242, 212, 138, 0.45); }
  .cap { position: absolute; bottom: 0; left: 50%; width: 40px; height: 11px; margin-left: -20px; border-radius: 2px; background: linear-gradient(180deg, #2b2b2e, #121214); box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.1), 0 1px 2px rgba(0, 0, 0, 0.8); }
  .push:not(:disabled):active .cap { transform: translateY(1px); box-shadow: inset 0 1px 2px rgba(0, 0, 0, 0.8); }
  .hint { grid-column: 1 / 3; font-size: 8px; color: var(--gold-dim); text-align: center; line-height: 1.3; }
  .unsupported { position: absolute; left: 50%; bottom: 14px; transform: translateX(-50%); z-index: 8; background: rgba(0, 0, 0, 0.85); border: 1px solid var(--gold-dim); padding: 6px 12px; border-radius: 4px; font-size: 11px; color: #f2e3bb; }
  /* Narrow: the analyser makes way for the faders */
  @media (max-width: 860px) { .centre { display: none; } }
  @media (max-width: 760px) { .model, .hint { display: none; } .cluster { width: 150px; gap: 5px 6px; } .power-column { width: 52px; } .labels { font-size: 7.5px; } }
</style>
