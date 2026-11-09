<script lang="ts">
  // Hi-fi mode (SPEC.md › Hi-fi mode): the whole window becomes a champagne-aluminium hi-fi separate, with meters, a VFD, transport keys and a volume knob
  import { app, ui, currentTrack, setHifi } from '../state.svelte'
  import { command, sendToShell } from '../bridge'
  import Meters from './Meters.svelte'
  const stageWidth = 1280, stageHeight = 330
  let windowWidth = $state(stageWidth), windowHeight = $state(stageHeight)
  const stageZoom = $derived(Math.min(windowWidth / stageWidth, windowHeight / stageHeight))
  const status = $derived(app.nowPlaying)
  const track = $derived(currentTrack())
  const playing = $derived(status?.mode === 'play')
  const duration = $derived(Number(status?.duration ?? track?.duration ?? 0))
  let now = $state(Date.now()), tick = $state(0)
  $effect(() => { const ticker = setInterval(() => { now = Date.now(); tick++ }, 250); return () => clearInterval(ticker) })
  let seekingTo = $state<number | null>(null)
  const position = $derived(seekingTo ?? Math.min(duration || Infinity, Number(status?.time ?? 0) + (playing ? (now - app.receivedAt) / 1000 : 0)))
  // Volume knob: drag up or right (or the wheel) to turn it up; the knob follows the hand at once, the player catches up
  let turningVolume = $state<number | null>(null)
  const volume = $derived(turningVolume ?? Math.abs(Number(status?.['mixer volume'] ?? 0)))
  const knobAngle = $derived(-135 + 270 * volume / 100)
  let knobGrab = null as null | { x: number, y: number, volume: number }, lastVolumeSent = 0, volumeReleaseTimer = 0
  const sendVolume = (level: number, force = false) => { turningVolume = level; if (force || Date.now() - lastVolumeSent > 60) { lastVolumeSent = Date.now(); command('mixer', 'volume', level) } }
  const releaseVolume = () => { clearTimeout(volumeReleaseTimer); volumeReleaseTimer = window.setTimeout(() => turningVolume = null, 1200) }
  function grabKnob(event: PointerEvent) { if (event.button !== 0) return; (event.currentTarget as Element).setPointerCapture(event.pointerId); clearTimeout(volumeReleaseTimer); knobGrab = { x: event.clientX, y: event.clientY, volume } }
  function turnKnob(event: PointerEvent) { if (!knobGrab) return; const travel = (event.clientX - knobGrab.x - (event.clientY - knobGrab.y)) / stageZoom; sendVolume(Math.max(0, Math.min(100, Math.round(knobGrab.volume + travel * 0.4)))) }
  function letGoKnob() { if (!knobGrab) return; knobGrab = null; if (turningVolume !== null) sendVolume(turningVolume, true); releaseVolume() }
  function wheelKnob(event: WheelEvent) { event.preventDefault(); clearTimeout(volumeReleaseTimer); sendVolume(Math.max(0, Math.min(100, volume + (event.deltaY < 0 ? 2 : -2))), true); releaseVolume() }
  // Position bar in the display: click or drag along it to seek
  let seekBar = $state<HTMLElement>()
  const seekRatio = (event: PointerEvent) => { const rect = seekBar!.getBoundingClientRect(); return Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width)) }
  let seekHeld = false
  function grabSeek(event: PointerEvent) { if (event.button !== 0 || !duration) return; seekHeld = true; (event.currentTarget as Element).setPointerCapture(event.pointerId); seekingTo = seekRatio(event) * duration }
  function dragSeek(event: PointerEvent) { if (seekingTo !== null) seekingTo = seekRatio(event) * duration }
  function letGoSeek() { seekHeld = false; if (seekingTo === null) return; command('time', Math.round(seekingTo)); const target = seekingTo; window.setTimeout(() => { if (seekingTo === target) seekingTo = null }, 1500) }
  $effect(() => { status; if (!seekHeld) seekingTo = null }) // the next status shows where it landed
  // Rewind and fast-forward: a click hops 10 s, holding scans in 5 s hops like the transport bar
  let scanTimer = 0, scanStart = 0
  function startScan(event: PointerEvent, direction: number) { if (event.button !== 0) return; stopScan(); scanStart = window.setTimeout(() => { const hop = () => command('time', direction > 0 ? '+5' : '-5'); hop(); scanTimer = window.setInterval(hop, 450) }, 500) }
  function stopScan() { clearTimeout(scanStart); clearInterval(scanTimer); scanStart = 0; scanTimer = 0 }
  function clickScan(direction: number) { if (scanTimer) { stopScan(); return } stopScan(); command('time', direction > 0 ? '+10' : '-10') }
  // Meter bridge: the real meters, zoomed to fit the window cut into the faceplate
  let meterBox = $state<HTMLElement>(), meterZoom = $state(1)
  // Fit from layout size (offsetWidth/Height): no zoom, transform or screen scaling can skew it, so the fit is the same on every monitor
  function fitMeters() { if (!meterBox) return; const naturalWidth = meterBox.offsetWidth, naturalHeight = meterBox.offsetHeight; if (naturalWidth && naturalHeight) meterZoom = Math.min(470 / naturalWidth, 166 / naturalHeight) }
  $effect(() => { meterBox; stageZoom; ui.meterStyle; requestAnimationFrame(fitMeters) })
  // Bare metal moves the window; right-click anywhere goes back to the normal player
  function pressPlate(event: MouseEvent) { if (event.button === 0 && !(event.target as HTMLElement).closest('button, .knob, .seek-zone')) sendToShell({ type: 'dragWindow' }) }
  // VFD: 5x7 dot matrix text, 7-segment digits and fixed indicators; unlit elements ghost faintly, as on a real tube
  const font: Record<string, number[]> = { A: [14,17,17,17,31,17,17], B: [30,17,17,30,17,17,30], C: [14,17,16,16,16,17,14], D: [28,18,17,17,17,18,28], E: [31,16,16,30,16,16,31], F: [31,16,16,30,16,16,16], G: [14,17,16,23,17,17,15], H: [17,17,17,31,17,17,17], I: [14,4,4,4,4,4,14], J: [7,2,2,2,2,18,12], K: [17,18,20,24,20,18,17], L: [16,16,16,16,16,16,31], M: [17,27,21,21,17,17,17], N: [17,17,25,21,19,17,17], O: [14,17,17,17,17,17,14], P: [30,17,17,30,16,16,16], Q: [14,17,17,17,21,18,13], R: [30,17,17,30,20,18,17], S: [15,16,16,14,1,1,30], T: [31,4,4,4,4,4,4], U: [17,17,17,17,17,17,14], V: [17,17,17,17,17,10,4], W: [17,17,17,21,21,21,10], X: [17,17,10,4,10,17,17], Y: [17,17,17,10,4,4,4], Z: [31,1,2,4,8,16,31], '0': [14,17,19,21,25,17,14], '1': [4,12,4,4,4,4,14], '2': [14,17,1,2,4,8,31], '3': [31,2,4,2,1,17,14], '4': [2,6,10,18,31,2,2], '5': [31,16,30,1,1,17,14], '6': [6,8,16,30,17,17,14], '7': [31,1,2,4,8,8,8], '8': [14,17,17,14,17,17,14], '9': [14,17,17,15,1,2,12], ':': [0,12,12,0,12,12,0], '.': [0,0,0,0,0,12,12], ',': [0,0,0,0,12,4,8], '-': [0,0,0,31,0,0,0], "'": [12,4,8,0,0,0,0], '"': [10,10,0,0,0,0,0], '/': [0,1,2,4,8,16,0], '·': [0,0,0,12,12,0,0], '(': [2,4,8,8,8,4,2], ')': [8,4,2,2,2,4,8], '&': [12,18,20,8,21,18,13], '!': [4,4,4,4,4,0,4], '?': [14,17,1,2,4,0,4], '+': [0,4,4,31,4,4,0], '#': [10,10,31,10,31,10,10], ' ': [0,0,0,0,0,0,0] }
  // Accented letters show as their plain letter (É -> E); anything else the tube can't draw becomes a space
  const plain = (text: string) => text.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[’‘]/g, "'").replace(/[“”]/g, '"').replace(/[–—]/g, '-').toUpperCase()
  const segmentsOf: Record<string, string> = { '0': 'abcdef', '1': 'bc', '2': 'abdeg', '3': 'abcdg', '4': 'bcfg', '5': 'acdfg', '6': 'acdefg', '7': 'abc', '8': 'abcdefg', '9': 'abcdfg', '-': 'g', ' ': '' }
  const formatNames: Record<string, string> = { flc: 'FLAC', flac: 'FLAC', alc: 'ALAC', mp3: 'MP3', aac: 'AAC', mp4: 'AAC', wav: 'WAV', aif: 'AIFF', ogg: 'OGG', ops: 'OPUS', wma: 'WMA', dsf: 'DSD', dff: 'DSD', wvp: 'WV', ape: 'APE' }
  let canvas = $state<HTMLCanvasElement>()
  const lit = '#8afce6', ghost = 'rgba(110, 255, 225, 0.075)', glow = 'rgba(70, 255, 215, 0.85)'
  const vfdWidth = 414, vfdHeight = 190
  // Long lines scroll through the matrix, pausing at the start
  const scrolled = (text: string, cells: number) => { if (text.length <= cells) return text; const loop = text + '     ', step = Math.max(0, tick - 8) % (loop.length + 8); return step >= loop.length ? text : (loop + loop).slice(step, step + cells) }
  function drawVfd() {
    if (!canvas) return
    const ratio = (window.devicePixelRatio || 1) * stageZoom
    if (canvas.width !== Math.round(vfdWidth * ratio)) { canvas.width = Math.round(vfdWidth * ratio); canvas.height = Math.round(vfdHeight * ratio) }
    const context = canvas.getContext('2d')!; context.setTransform(ratio, 0, 0, ratio, 0, 0); context.clearRect(0, 0, vfdWidth, vfdHeight)
    const paint = (on: boolean) => { context.fillStyle = on ? lit : ghost; context.shadowColor = on ? glow : 'transparent'; context.shadowBlur = on ? 7 : 0 }
    const dotLine = (text: string, left: number, top: number, pitch: number, cells: number) => {
      const shown = text.padEnd(cells, ' ').slice(0, cells)
      for (let cell = 0; cell < cells; cell++) { const rows = font[shown[cell]] ?? font[' ']; for (let row = 0; row < 7; row++) for (let column = 0; column < 5; column++) { paint(!!(rows[row] & (16 >> column))); context.fillRect(left + (cell * 6 + column) * pitch, top + row * pitch, pitch * 0.78, pitch * 0.78) } }
    }
    // 7-segment digit, slanted like a real tube
    const digit = (character: string, left: number, top: number, width: number, height: number, thickness: number) => {
      const on = segmentsOf[character] ?? '', half = height / 2, gap = thickness * 0.18
      const horizontal = (y: number) => [[left + gap + thickness / 2, y], [left + thickness + gap, y - thickness / 2], [left + width - thickness - gap, y - thickness / 2], [left + width - gap - thickness / 2, y], [left + width - thickness - gap, y + thickness / 2], [left + thickness + gap, y + thickness / 2]]
      const vertical = (x: number, fromY: number, toY: number) => [[x, fromY + gap], [x + thickness / 2, fromY + gap + thickness / 2], [x + thickness / 2, toY - gap - thickness / 2], [x, toY - gap], [x - thickness / 2, toY - gap - thickness / 2], [x - thickness / 2, fromY + gap + thickness / 2]]
      const shapes: Record<string, number[][]> = { a: horizontal(top), g: horizontal(top + half), d: horizontal(top + height), f: vertical(left + thickness / 2, top, top + half), b: vertical(left + width - thickness / 2, top, top + half), e: vertical(left + thickness / 2, top + half, top + height), c: vertical(left + width - thickness / 2, top + half, top + height) }
      context.save(); context.translate(left + width / 2, top + half); context.transform(1, 0, -0.1, 1, 0, 0); context.translate(-(left + width / 2), -(top + half))
      for (const name of 'abcdefg') { paint(on.includes(name)); context.beginPath(); shapes[name].forEach(([x, y], index) => index ? context.lineTo(x, y) : context.moveTo(x, y)); context.closePath(); context.fill() }
      context.restore()
    }
    const label = (text: string, left: number, top: number, on: boolean, align: CanvasTextAlign = 'left') => { paint(on); context.font = '600 9.5px "Segoe UI", Inter, sans-serif'; context.letterSpacing = '1.4px'; context.textAlign = align; context.fillText(text, left, top) }
    // Indicators: play, pause, repeat, shuffle; the file's format, rate and depth on the right
    paint(playing); context.beginPath(); context.moveTo(18, 14); context.lineTo(26, 18.5); context.lineTo(18, 23); context.fill()
    paint(!!track && !playing); context.fillRect(34, 14, 3, 9); context.fillRect(40, 14, 3, 9)
    label('REPEAT', 58, 23, Number(status?.['playlist repeat'] ?? 0) > 0); label('SHUFFLE', 112, 23, Number(status?.['playlist shuffle'] ?? 0) > 0)
    const format = formatNames[String(track?.type ?? '').toLowerCase()] ?? String(track?.type ?? '').toUpperCase().slice(0, 4), rate = Number(track?.samplerate ?? 0), depth = Number(track?.samplesize ?? 0)
    label(depth ? `${depth}bit` : '', vfdWidth - 18, 23, true, 'right'); label(rate ? `${(rate / 1000).toFixed(rate % 1000 ? 1 : 0)}kHz` : '', vfdWidth - 72, 23, true, 'right'); label(format, vfdWidth - 132, 23, true, 'right')
    // Track number and elapsed time
    const trackNumber = track ? String(Number(status?.playlist_cur_index ?? 0) + 1).padStart(2, '0').slice(-2) : '--'
    label('TRACK', 18, 42, true); digit(trackNumber[0], 18, 48, 22, 36, 4.6); digit(trackNumber[1], 46, 48, 22, 36, 4.6)
    const seconds = Math.max(0, Math.floor(position)), minutes = Math.min(99, Math.floor(seconds / 60)), timeDigits = track ? (String(minutes) + String(seconds % 60).padStart(2, '0')).padStart(4, ' ') : '----', timeRight = vfdWidth - 18
    label('TIME', timeRight, 42, true, 'right')
    digit(timeDigits[3], timeRight - 22, 48, 22, 36, 4.6); digit(timeDigits[2], timeRight - 50, 48, 22, 36, 4.6); digit(timeDigits[1], timeRight - 96, 48, 22, 36, 4.6); digit(timeDigits[0], timeRight - 124, 48, 22, 36, 4.6)
    paint(!!track); context.fillRect(timeRight - 65, 58, 4.2, 4.2); context.fillRect(timeRight - 66, 72, 4.2, 4.2)
    // Title, then artist and album, in the dot matrix
    const pitch = 2.45, cells = Math.floor((vfdWidth - 36) / (6 * pitch))
    const title = plain(track?.title ?? (app.status?.message || 'NO DISC')), byline = plain([track?.artist ?? track?.albumartist, track?.album].filter(Boolean).join(' · '))
    dotLine(scrolled(title, cells), 18, 100, pitch, cells); dotLine(scrolled(byline, cells), 18, 100 + 7 * pitch + 9, pitch, cells)
    // Position bar
    const segments = 56, barWidth = vfdWidth - 36, share = duration ? position / duration : 0
    for (let segment = 0; segment < segments; segment++) { paint(segment / segments < share); context.fillRect(18 + segment * barWidth / segments, vfdHeight - 19, barWidth / segments - 2, 5) }
  }
  $effect(() => { status; track; playing; position; tick; stageZoom; canvas; drawVfd() })
  const keys: [string, string, string][] = [['previous', 'Previous track', 'M6 5h2v14H6zM9.5 12 19 5v14z'], ['rewind', 'Rewind (hold to scan)', 'M11 5v14L2 12zM21 5v14l-9-7z'], ['play', 'Play or pause', 'M3 5v14l9-7zM14 5h3v14h-3zM19 5h3v14h-3z'], ['forward', 'Fast-forward (hold to scan)', 'M3 5v14l9-7zM13 5v14l9-7z'], ['next', 'Next track', 'M5 5v14l9.5-7zM16 5h2v14h-2z']]
  function pressKey(name: string) {
    if (name === 'previous') command('playlist', 'index', '-1')
    else if (name === 'next') command('playlist', 'index', '+1')
    else if (name === 'play') command('pause', playing ? '1' : '0')
    else clickScan(name === 'forward' ? 1 : -1)
  }
</script>

<svelte:window bind:innerWidth={windowWidth} bind:innerHeight={windowHeight} />
<!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
<div class="hifi" role="dialog" aria-label="Hi-fi" tabindex="-1" onmousedown={pressPlate} oncontextmenu={(event) => { event.preventDefault(); setHifi(false) }}>
  <div class="plate"></div>
  <div class="stage" style:transform="translate(-50%, -50%) scale({stageZoom})">
    <div class="brand">WinSqueeze</div>
    <div class="standby" class:on={playing}></div>
    <div class="meter-window"><div class="meter-zoom" bind:this={meterBox} style:transform="scale({meterZoom})"><Meters size="large" zoom={meterZoom * stageZoom} moveWindow={true} /></div><div class="pane"></div></div>
    <div class="display">
      <canvas bind:this={canvas} style:width="{vfdWidth}px" style:height="{vfdHeight}px"></canvas><div class="mesh"></div><div class="pane"></div>
      <div class="seek-zone" bind:this={seekBar} role="slider" tabindex="-1" aria-label="Position" aria-valuenow={Math.round(position)} title="Click or drag to move through the track" onpointerdown={grabSeek} onpointermove={dragSeek} onpointerup={letGoSeek}></div>
    </div>
    <div class="keys">{#each keys as [name, label, path]}<button class="key" aria-label={label} title={label} onclick={() => pressKey(name)} onpointerdown={(event) => { if (name === 'rewind' || name === 'forward') startScan(event, name === 'forward' ? 1 : -1) }} onpointerup={() => { if (scanStart && !scanTimer) stopScan() }} onpointerleave={() => { if (scanTimer) stopScan() }}><svg viewBox="0 0 24 24"><path d={path} /></svg></button>{/each}</div>
    <div class="knob-well">
      <svg class="scale" viewBox="0 0 260 260">{#each Array.from({ length: 31 }, (unused, index) => -135 + index * 9) as angle, index}<line x1="130" y1={index % 5 ? 14 : 10} x2="130" y2="22" transform="rotate({angle} 130 130)" class:major={!(index % 5)} />{/each}</svg>
      <div class="knob" role="slider" tabindex="-1" aria-label="Volume" aria-valuenow={volume} title="Volume {volume} (drag up or down, or scroll)" style:--angle="{knobAngle}deg" onpointerdown={grabKnob} onpointermove={turnKnob} onpointerup={letGoKnob} onwheel={wheelKnob}><div class="teeth"></div><div class="knurl"></div><div class="cap"></div><div class="pointer"><div class="mark"></div></div></div>
    </div>
    <div class="volume-label">VOLUME</div>
    <!-- Power: a machined button set into the plate, back to the normal player -->
    <button class="power" onclick={() => setHifi(false)} title="Back to the player (Esc or right-click)" aria-label="Back to the player"><svg viewBox="0 0 24 24"><path d="M12 3.5v8" /><path d="M7.2 6.6a7.5 7.5 0 1 0 9.6 0" /></svg></button>
  </div>
  </div>

<style>
  .hifi { position: fixed; inset: 0; z-index: 90; overflow: hidden; outline: none; user-select: none; }
  :global(html.frameless:not(.maximised)) .hifi { border-radius: 8px; }
  /* Champagne aluminium, like late-70s hi-fi: stretched noise for the brushed grain, a soft light falling across it, chamfered top and bottom edges */
  .plate { position: absolute; inset: 0; background:
      url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='1400' height='500'%3E%3Cfilter id='b'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.0018 0.75' numOctaves='3' seed='7'/%3E%3CfeColorMatrix values='0 0 0 0 0.5 0 0 0 0 0.47 0 0 0 0 0.43 0 0 0 1.1 -0.32'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23b)'/%3E%3C/svg%3E") 0 0 / 1400px 500px,
      linear-gradient(100deg, rgba(255,255,255,0) 20%, rgba(255,246,230,0.26) 42%, rgba(255,255,255,0) 60%),
      linear-gradient(180deg, #f6f0e8 0, #d6cbbb 2px, #ddd2c1 8%, #cbbfab 55%, #bbae98 96%, #8c8170 calc(100% - 3px), #5d5447 100%);
    background-blend-mode: overlay, normal, normal; box-shadow: inset 0 1px 0 rgba(255,255,255,0.9), inset 0 -2px 2px rgba(0,0,0,0.25); }
  /* Scaled with a transform, not zoom: the meter fit below measures through it the same way in every WebView2 version */
  .stage { position: absolute; left: 50%; top: 50%; width: 1280px; height: 330px; --pad: 34px; }
  .brand { position: absolute; left: var(--pad); bottom: 43px; font: 600 15px/1 "Segoe UI", Inter, sans-serif; letter-spacing: 3px; color: #4a3b28; text-shadow: 0 1px 0 rgba(255,245,225,0.75); pointer-events: none; }
  .standby { position: absolute; left: calc(var(--pad) + 160px); bottom: 46px; width: 7px; height: 7px; border-radius: 50%; background: radial-gradient(circle at 40% 35%, #c9ffd9, #1fd25f 45%, #0b6b2c); box-shadow: 0 0 6px 2px rgba(60, 255, 120, 0.55), 0 1px 0 rgba(255,255,255,0.7); }
  .standby:not(.on) { background: radial-gradient(circle at 40% 35%, #ffd1c4, #c53a1c 50%, #5c140a); box-shadow: 0 0 5px 1px rgba(255, 70, 30, 0.45); }
  /* Window cut into the plate: black recess, the two meters behind smoked glass */
  .meter-window { position: absolute; left: var(--pad); top: 34px; width: 490px; height: 190px; border-radius: 6px; background: #0b0b0c; box-shadow: 0 -1px 0 rgba(0,0,0,0.35), 0 1px 0 rgba(255,255,255,0.85), inset 0 3px 8px rgba(0,0,0,0.9); display: grid; place-items: center; overflow: hidden; }
  .pane { position: absolute; inset: 0; border-radius: inherit; pointer-events: none; background: linear-gradient(160deg, rgba(255,255,255,0.10) 0%, rgba(255,255,255,0.03) 28%, rgba(255,255,255,0) 29%, rgba(255,255,255,0) 100%); box-shadow: inset 0 0 0 1px rgba(255,255,255,0.06); }
  /* VFD behind dark tinted acrylic, with the tube's fine grid mesh */
  .display { position: absolute; left: 552px; width: 414px; top: 34px; height: 190px; border-radius: 6px; background: radial-gradient(120% 90% at 50% 40%, #071513 0%, #030807 70%); box-shadow: 0 -1px 0 rgba(0,0,0,0.35), 0 1px 0 rgba(255,255,255,0.85), inset 0 3px 10px rgba(0,0,0,0.95); overflow: hidden; }
  .display canvas { position: absolute; left: 0; top: 0; pointer-events: none; }
  .mesh { position: absolute; inset: 0; pointer-events: none; background: repeating-linear-gradient(0deg, rgba(0,0,0,0.22) 0 1px, transparent 1px 3px), repeating-linear-gradient(90deg, rgba(0,0,0,0.12) 0 1px, transparent 1px 3px); }
  .seek-zone { position: absolute; left: 10px; right: 10px; bottom: 6px; height: 22px; cursor: pointer; outline: none; }
  /* Transport keys: machined champagne aluminium, a dark gap around each, icons engraved */
  .keys { position: absolute; left: 552px; width: 414px; bottom: 28px; display: flex; justify-content: space-between; gap: 10px; }
  .key { flex: 1; height: 44px; border: 0; border-radius: 5px; cursor: pointer; display: grid; place-items: center;
    background: linear-gradient(180deg, #faf4ec 0%, #e2d7c6 45%, #d1c4b0 55%, #bcae98 100%);
    box-shadow: 0 0 0 1.5px #3b3022, 0 0 0 3px rgba(0,0,0,0.25), 0 3px 4px rgba(0,0,0,0.35), inset 0 1px 0 #fff8ec, inset 0 -2px 2px rgba(0,0,0,0.15); }
  .key svg { width: 22px; height: 22px; } .key path { fill: #4a3b28; filter: drop-shadow(0 1px 0 rgba(255,245,225,0.8)); }
  .key:active { transform: translateY(1px); background: linear-gradient(180deg, #d4c8b5 0%, #c8bba6 50%, #bcae98 100%); box-shadow: 0 0 0 1.5px #3b3022, 0 0 0 3px rgba(0,0,0,0.25), 0 1px 1px rgba(0,0,0,0.3), inset 0 2px 3px rgba(0,0,0,0.25); }
  .key:focus-visible { outline: 2px solid rgba(74, 59, 40, 0.6); outline-offset: 4px; }
  /* Volume: printed scale on the plate, a heavy turned knob with a knurled rim and a sunburst-finished cap */
  .knob-well { position: absolute; right: 30px; top: 50%; translate: 0 -54%; width: 260px; height: 260px; }
  .scale { position: absolute; inset: 0; pointer-events: none; } .scale line { stroke: #5a4934; stroke-width: 1.4; } .scale line.major { stroke-width: 2.2; stroke: #3b2f20; }
  /* The light and the shadow stay put while the knob turns: only the knurled teeth and the pointer rotate (the turned cap's sheen doesn't move on a real knob either) */
  .knob { position: absolute; inset: 26px; border-radius: 50%; cursor: grab; touch-action: none; outline: none;
    box-shadow: 0 14px 22px rgba(0,0,0,0.45), 0 4px 6px rgba(0,0,0,0.35), 0 0 0 1px rgba(0,0,0,0.35); }
  .knob:active { cursor: grabbing; }
  .teeth { position: absolute; inset: 0; border-radius: 50%; rotate: var(--angle); background: repeating-conic-gradient(from 0deg, #8f8370 0deg 2deg, #dcd0bd 2deg 4deg); }
  .knurl { position: absolute; inset: 0; border-radius: 50%; background: linear-gradient(180deg, rgba(255,255,255,0.45), rgba(0,0,0,0.35)); mix-blend-mode: overlay; }
  .cap { position: absolute; inset: 9px; border-radius: 50%;
    background: conic-gradient(from 0deg, #f1e9dc, #aa9d88 12%, #fbf7f0 25%, #afa28d 38%, #ebe1d1 50%, #a59883 62%, #f9f3ea 75%, #b2a590 88%, #f1e9dc);
    box-shadow: inset 0 2px 1px rgba(255,248,232,0.8), inset 0 -2px 3px rgba(0,0,0,0.35), 0 0 0 1px rgba(0,0,0,0.25); }
  .cap::after { content: ''; position: absolute; inset: 0; border-radius: 50%; background: repeating-radial-gradient(circle at 50% 50%, rgba(0,0,0,0.06) 0 0.6px, rgba(255,255,255,0.07) 0.6px 1.4px); }
  .pointer { position: absolute; inset: 0; rotate: var(--angle); pointer-events: none; }
  .mark { position: absolute; left: 50%; top: 18px; width: 9px; height: 9px; margin-left: -4.5px; border-radius: 50%; rotate: calc(-1 * var(--angle)); background: radial-gradient(circle at 50% 70%, #7a6548, #31271a); box-shadow: inset 0 1px 2px rgba(0,0,0,0.7), 0 1px 0 rgba(255,255,255,0.7); }
  .volume-label { position: absolute; right: 30px; width: 260px; text-align: center; bottom: 26px; font: 600 9.5px/1 "Segoe UI", Inter, sans-serif; letter-spacing: 3px; color: #5a4934; text-shadow: 0 1px 0 rgba(255,245,225,0.7); pointer-events: none; }
  .power { position: absolute; top: 14px; right: 16px; width: 30px; height: 30px; border: 0; border-radius: 50%; padding: 0; cursor: pointer; display: grid; place-items: center;
    background: radial-gradient(circle at 50% 30%, #fdf9f2 0%, #e4d9c8 45%, #c6b8a2 100%);
    box-shadow: 0 0 0 1.5px #3b3022, 0 0 0 3.5px rgba(0,0,0,0.16), 0 0 0 4.5px rgba(255,248,232,0.55), 0 2px 3px rgba(0,0,0,0.4), inset 0 1px 0 #fff8ec, inset 0 -2px 2px rgba(0,0,0,0.18); }
  .power svg { width: 15px; height: 15px; } .power path { fill: none; stroke: #4a3b28; stroke-width: 2.3; stroke-linecap: round; filter: drop-shadow(0 1px 0 rgba(255,245,225,0.8)); }
  .power:active { transform: translateY(1px); background: radial-gradient(circle at 50% 40%, #e6dccb 0%, #d0c3ae 60%, #bcae98 100%); box-shadow: 0 0 0 1.5px #3b3022, 0 0 0 3.5px rgba(0,0,0,0.16), 0 0 0 4.5px rgba(255,248,232,0.55), 0 1px 1px rgba(0,0,0,0.3), inset 0 2px 3px rgba(0,0,0,0.25); }
  .power:focus-visible { outline: 2px solid rgba(74, 59, 40, 0.6); outline-offset: 5px; }
</style>
