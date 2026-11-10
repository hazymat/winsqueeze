<script lang="ts">
  import { t } from '../lib/i18n'
  // Hi-fi mode (SPEC.md › Hi-fi mode): the whole window becomes a champagne-aluminium hi-fi separate, with meters, a VFD, transport keys and a volume knob
  import { app, ui, currentTrack, setHifi, readSetting, writeSetting } from '../state.svelte'
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
  // Meters: the real meters, zoomed to fill the left of the faceplate, each set into its own cut-out
  let meterBox = $state<HTMLElement>(), meterZoom = $state(1)
  // Fit from layout size (offsetWidth/Height): no zoom, transform or screen scaling can skew it, so the fit is the same on every monitor
  function fitMeters() { if (!meterBox) return; const naturalWidth = meterBox.offsetWidth, naturalHeight = meterBox.offsetHeight; if (naturalWidth && naturalHeight) meterZoom = Math.min(490 / naturalWidth, 190 / naturalHeight) }
  $effect(() => { meterBox; stageZoom; ui.meterStyle; requestAnimationFrame(fitMeters) })
  // Bare metal moves the window; right-click anywhere goes back to the normal player
  function pressPlate(event: MouseEvent) { if (event.button === 0 && !(event.target as HTMLElement).closest('button, .knob, .seek-zone, .clickable')) sendToShell({ type: 'dragWindow' }) }
  // Firmware-style update (SPEC.md › Hi-fi mode): the standby lamp pulses blue when an update is ready; hovering it shows the update on the display with an Update key;
  // pressing it plays a short "upgrading firmware" sequence before the updater takes over, and the unit comes back after the restart saying what it was updated to
  const updateReady = $derived(String(app.status?.updateReady ?? ''))
  let updateHover = $state(false), hoverTimer = 0, upgradeStartedAt = $state(0), animationNow = $state(0)
  const enterUpdate = () => { clearTimeout(hoverTimer); if (updateReady) updateHover = true }
  const leaveUpdate = () => { clearTimeout(hoverTimer); hoverTimer = window.setTimeout(() => updateHover = false, 900) }
  function startUpgrade() {
    if (upgradeStartedAt) return
    writeSetting('hifiUpdateFrom', String(app.status?.version ?? '')) // reopen in hi-fi after the restart, to say what changed
    upgradeStartedAt = animationNow = Date.now()
    const frames = window.setInterval(() => { animationNow = Date.now(); if (animationNow - upgradeStartedAt > 2700) { clearInterval(frames); sendToShell({ type: 'applyUpdate' }) } }, 40)
  }
  let switchThrown = $state(false)
  function throwSwitch() { if (switchThrown) return; switchThrown = true; window.setTimeout(() => setHifi(false), 260) } // let the lever finish its throw first
  // After the restart: shown on the display for a while, a click on it goes to the normal player where the What's new card is waiting
  const updatedFrom = readSetting('hifiUpdateFrom', '')
  let updatedNoticeUntil = $state(0)
  $effect(() => { const version = String(app.status?.version ?? ''); if (!updatedFrom || !version) return; writeSetting('hifiUpdateFrom', ''); if (version !== updatedFrom) updatedNoticeUntil = Date.now() + 20000 })
  const showingUpdated = $derived(updatedNoticeUntil > now)
  function openWhatsNew() { updatedNoticeUntil = 0; setHifi(false) }
  // What the display shows: the music, or one of the update screens
  const screen = $derived.by(() => {
    const normalTrack = track ? String(Number(status?.playlist_cur_index ?? 0) + 1).padStart(2, '0').slice(-2) : '--'
    const seconds = Math.max(0, Math.floor(position)), minutes = Math.min(99, Math.floor(seconds / 60)), normalTime = track ? (String(minutes) + String(seconds % 60).padStart(2, '0')).padStart(4, ' ') : '----'
    const version = String(app.status?.version ?? '')
    if (upgradeStartedAt) {
      const elapsed = animationNow - upgradeStartedAt, percent = Math.max(0, Math.min(100, Math.round((elapsed - 400) / 20)))
      if (elapsed < 400) return { mode: 'upgrading', blank: true, trackDigits: '  ', timeDigits: '    ', colon: false, line1: '', line2: '', share: 0 }
      if (percent >= 100) return { mode: 'upgrading', blank: false, trackDigits: 'UP', timeDigits: ' 100'.slice(-4), colon: false, line1: 'RESTARTING', line2: 'PLEASE WAIT', share: 1 }
      return { mode: 'upgrading', blank: false, trackDigits: 'UP', timeDigits: String(percent).padStart(4, ' '), colon: false, line1: 'UPGRADING FIRMWARE', line2: `V${version} > V${updateReady}`, share: percent / 100 }
    }
    if (updateReady && updateHover) return { mode: 'prompt', blank: false, trackDigits: '  ', timeDigits: '    ', colon: false, line1: `V${updateReady} READY`, line2: 'PRESS UPDATE TO INSTALL', share: 0 }
    if (showingUpdated) return { mode: 'updated', blank: false, trackDigits: 'UP', timeDigits: '    ', colon: false, line1: `UPDATED TO V${version}`, line2: "CLICK HERE FOR WHAT'S NEW", share: 0 }
    return { mode: 'normal', blank: false, trackDigits: normalTrack, timeDigits: normalTime, colon: !!track, line1: track?.title ?? (app.status?.message || 'NO DISC'), line2: [track?.artist ?? track?.albumartist, track?.album].filter(Boolean).join(' · '), share: duration ? position / duration : 0 }
  })
  // VFD: 5x7 dot matrix text, 7-segment digits and fixed indicators; unlit elements ghost faintly, as on a real tube
  const font: Record<string, number[]> = { A: [14,17,17,17,31,17,17], B: [30,17,17,30,17,17,30], C: [14,17,16,16,16,17,14], D: [28,18,17,17,17,18,28], E: [31,16,16,30,16,16,31], F: [31,16,16,30,16,16,16], G: [14,17,16,23,17,17,15], H: [17,17,17,31,17,17,17], I: [14,4,4,4,4,4,14], J: [7,2,2,2,2,18,12], K: [17,18,20,24,20,18,17], L: [16,16,16,16,16,16,31], M: [17,27,21,21,17,17,17], N: [17,17,25,21,19,17,17], O: [14,17,17,17,17,17,14], P: [30,17,17,30,16,16,16], Q: [14,17,17,17,21,18,13], R: [30,17,17,30,20,18,17], S: [15,16,16,14,1,1,30], T: [31,4,4,4,4,4,4], U: [17,17,17,17,17,17,14], V: [17,17,17,17,17,10,4], W: [17,17,17,21,21,21,10], X: [17,17,10,4,10,17,17], Y: [17,17,17,10,4,4,4], Z: [31,1,2,4,8,16,31], '0': [14,17,19,21,25,17,14], '1': [4,12,4,4,4,4,14], '2': [14,17,1,2,4,8,31], '3': [31,2,4,2,1,17,14], '4': [2,6,10,18,31,2,2], '5': [31,16,30,1,1,17,14], '6': [6,8,16,30,17,17,14], '7': [31,1,2,4,8,8,8], '8': [14,17,17,14,17,17,14], '9': [14,17,17,15,1,2,12], ':': [0,12,12,0,12,12,0], '.': [0,0,0,0,0,12,12], ',': [0,0,0,0,12,4,8], '-': [0,0,0,31,0,0,0], "'": [12,4,8,0,0,0,0], '"': [10,10,0,0,0,0,0], '/': [0,1,2,4,8,16,0], '·': [0,0,0,12,12,0,0], '(': [2,4,8,8,8,4,2], ')': [8,4,2,2,2,4,8], '&': [12,18,20,8,21,18,13], '>': [16,8,4,2,4,8,16], '!': [4,4,4,4,4,0,4], '?': [14,17,1,2,4,0,4], '+': [0,4,4,31,4,4,0], '#': [10,10,31,10,31,10,10], ' ': [0,0,0,0,0,0,0] }
  // Accented letters show as their plain letter (É -> E); anything else the tube can't draw becomes a space
  const plain = (text: string) => text.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[’‘]/g, "'").replace(/[“”]/g, '"').replace(/[–—]/g, '-').toUpperCase()
  const segmentsOf: Record<string, string> = { '0': 'abcdef', '1': 'bc', '2': 'abdeg', '3': 'abcdg', '4': 'bcfg', '5': 'acdfg', '6': 'acdefg', '7': 'abc', '8': 'abcdefg', '9': 'abcdfg', '-': 'g', ' ': '', U: 'bcdef', P: 'abefg' }
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
    const paint = (wanted: boolean) => { const on = wanted && !screen.blank; context.fillStyle = on ? lit : ghost; context.shadowColor = on ? glow : 'transparent'; context.shadowBlur = on ? 7 : 0 }
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
    label('REPEAT', 58, 23, Number(status?.['playlist repeat'] ?? 0) > 0); label('SHUFFLE', 112, 23, Number(status?.['playlist shuffle'] ?? 0) > 0); label('UPDATE', 178, 23, !!updateReady && (screen.mode !== 'normal' || tick % 4 < 2))
    const format = formatNames[String(track?.type ?? '').toLowerCase()] ?? String(track?.type ?? '').toUpperCase().slice(0, 4), rate = Number(track?.samplerate ?? 0), depth = Number(track?.samplesize ?? 0)
    label(depth ? `${depth}bit` : '', vfdWidth - 18, 23, true, 'right'); label(rate ? `${(rate / 1000).toFixed(rate % 1000 ? 1 : 0)}kHz` : '', vfdWidth - 72, 23, true, 'right'); label(format, vfdWidth - 132, 23, true, 'right')
    // Track number and elapsed time
    const trackNumber = screen.trackDigits, timeDigits = screen.timeDigits, timeRight = vfdWidth - 18
    label('TRACK', 18, 42, screen.mode === 'normal'); digit(trackNumber[0], 18, 48, 22, 36, 4.6); digit(trackNumber[1], 46, 48, 22, 36, 4.6)
    label(screen.mode === 'upgrading' ? '%' : 'TIME', timeRight, 42, screen.mode === 'normal' || screen.mode === 'upgrading', 'right')
    digit(timeDigits[3], timeRight - 22, 48, 22, 36, 4.6); digit(timeDigits[2], timeRight - 50, 48, 22, 36, 4.6); digit(timeDigits[1], timeRight - 96, 48, 22, 36, 4.6); digit(timeDigits[0], timeRight - 124, 48, 22, 36, 4.6)
    paint(screen.colon); context.fillRect(timeRight - 65, 58, 4.2, 4.2); context.fillRect(timeRight - 66, 72, 4.2, 4.2)
    // Title, then artist and album, in the dot matrix
    const pitch = 2.45, cells = Math.floor((vfdWidth - 36) / (6 * pitch))
    const title = plain(screen.line1), byline = plain(screen.line2)
    dotLine(scrolled(title, cells), 18, 100, pitch, cells); dotLine(scrolled(byline, cells), 18, 100 + 7 * pitch + 9, pitch, cells)
    // Position bar
    const segments = 56, barWidth = vfdWidth - 36, share = screen.share
    for (let segment = 0; segment < segments; segment++) { paint(segment / segments < share); context.fillRect(18 + segment * barWidth / segments, vfdHeight - 19, barWidth / segments - 2, 5) }
  }
  $effect(() => { screen; tick; stageZoom; canvas; drawVfd() })
  const keys: [string, string, string][] = [['previous', t('Previous track'), 'M6 5h2v14H6zM9.5 12 19 5v14z'], ['rewind', t('Rewind (hold to scan)'), 'M11 5v14L2 12zM21 5v14l-9-7z'], ['play', t('Play or pause'), 'M3 5v14l9-7zM14 5h3v14h-3zM19 5h3v14h-3z'], ['forward', t('Fast-forward (hold to scan)'), 'M3 5v14l9-7zM13 5v14l9-7z'], ['next', t('Next track'), 'M5 5v14l9.5-7zM16 5h2v14h-2z']]
  function pressKey(name: string) {
    if (name === 'previous') command('playlist', 'index', '-1')
    else if (name === 'next') command('playlist', 'index', '+1')
    else if (name === 'play') command('pause', playing ? '1' : '0')
    else clickScan(name === 'forward' ? 1 : -1)
  }
</script>

<svelte:window bind:innerWidth={windowWidth} bind:innerHeight={windowHeight} />
<!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
<div class="hifi" role="dialog" aria-label={t('Hi-fi')} tabindex="-1" onmousedown={pressPlate} oncontextmenu={(event) => { event.preventDefault(); setHifi(false) }}>
  <div class="plate"></div>
  <div class="stage" style:transform="translate(-50%, -50%) scale({stageZoom})">
    <div class="update-zone" role="presentation" onpointerenter={enterUpdate} onpointerleave={leaveUpdate}>
      <div class="brand">WinSqueeze</div>
      <div class="standby" class:on={playing} class:update={!!updateReady} class:upgrading={!!upgradeStartedAt} title={updateReady ? t('WinSqueeze {version} is ready', { version: updateReady }) : ''}></div>
      {#if updateReady}<button class="update-key" class:shown={updateHover || !!upgradeStartedAt} onclick={startUpgrade} title={t('Restart to update to {version}', { version: updateReady })}>UPDATE</button>{/if}
    </div>
    <div class="meter-window"><div class="meter-zoom" bind:this={meterBox} style:transform="scale({meterZoom})"><Meters size="large" zoom={meterZoom * stageZoom} moveWindow={true} hifi={true} /></div></div>
    <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
    <div class="display" class:clickable={showingUpdated} onclick={(event) => { if (showingUpdated && !(event.target as HTMLElement).closest('.seek-zone')) openWhatsNew() }}>
      <canvas bind:this={canvas} style:width="{vfdWidth}px" style:height="{vfdHeight}px"></canvas><div class="mesh"></div><div class="pane"></div>
      <div class="seek-zone" bind:this={seekBar} role="slider" tabindex="-1" aria-label={t('Position')} aria-valuenow={Math.round(position)} title={t('Click or drag to move through the track')} onpointerdown={grabSeek} onpointermove={dragSeek} onpointerup={letGoSeek}></div>
    </div>
    <div class="keys">{#each keys as [name, label, path]}<button class="key" aria-label={label} title={label} onclick={() => pressKey(name)} onpointerdown={(event) => { if (name === 'rewind' || name === 'forward') startScan(event, name === 'forward' ? 1 : -1) }} onpointerup={() => { if (scanStart && !scanTimer) stopScan() }} onpointerleave={() => { if (scanTimer) stopScan() }}><svg viewBox="0 0 24 24"><path d={path} /></svg></button>{/each}</div>
    <div class="knob-well">
      <svg class="scale" viewBox="0 0 260 260">{#each Array.from({ length: 31 }, (unused, index) => -135 + index * 9) as angle, index}<line x1="130" y1={index % 5 ? 14 : 10} x2="130" y2="22" transform="rotate({angle} 130 130)" class:major={!(index % 5)} />{/each}</svg>
      <div class="knob" role="slider" tabindex="-1" aria-label={t('Volume')} aria-valuenow={volume} title={t('Volume {volume} (drag up or down, or scroll)', { volume })} style:--angle="{knobAngle}deg" onpointerdown={grabKnob} onpointermove={turnKnob} onpointerup={letGoKnob} onwheel={wheelKnob}><div class="teeth"></div><div class="knurl"></div><div class="cap"></div><div class="pointer"><div class="mark"></div></div></div>
    </div>
    <div class="volume-label">{t('VOLUME')}</div>
    <!-- Mode toggle: chrome bat-handle switch, flicked down (animated throw) goes back to the library -->
    <button class="mode-switch" class:thrown={switchThrown} onclick={throwSwitch} title={t('Back to the library (Esc or right-click)')} aria-label={t('Back to the library')}><span class="engraving">HI-FI</span><span class="switch-body"><span class="nut"></span><span class="collar"></span><span class="lever"><span class="lever-shaft"></span><span class="lever-tip"></span></span></span><span class="engraving">{t('LIBRARY')}</span></button>
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
  .update-zone { position: absolute; left: var(--pad); bottom: 26px; width: 300px; height: 40px; }
  .brand { position: absolute; left: 0; bottom: 17px; font: 600 15px/1 "Segoe UI", Inter, sans-serif; letter-spacing: 3px; color: #4a3b28; text-shadow: 0 1px 0 rgba(255,245,225,0.75); pointer-events: none; }
  .standby { position: absolute; left: 160px; bottom: 20px; width: 7px; height: 7px; border-radius: 50%; background: radial-gradient(circle at 40% 35%, #c9ffd9, #1fd25f 45%, #0b6b2c); box-shadow: 0 0 6px 2px rgba(60, 255, 120, 0.55), 0 1px 0 rgba(255,255,255,0.7); }
  .standby:not(.on) { background: radial-gradient(circle at 40% 35%, #ffd1c4, #c53a1c 50%, #5c140a); box-shadow: 0 0 5px 1px rgba(255, 70, 30, 0.45); }
  /* Update ready: the lamp turns blue and breathes; faster while upgrading */
  .standby.update { background: radial-gradient(circle at 40% 35%, #d9eeff, #2f8cff 45%, #0b3a8c); animation: update-pulse 1.6s ease-in-out infinite; }
  .standby.upgrading { animation-duration: 0.35s; }
  @keyframes update-pulse { 0%, 100% { box-shadow: 0 0 3px 1px rgba(60, 140, 255, 0.35); filter: brightness(0.75); } 50% { box-shadow: 0 0 10px 3px rgba(60, 150, 255, 0.85); filter: brightness(1.3); } }
  .update-key { position: absolute; left: 184px; bottom: 9px; height: 26px; padding: 0 12px; border: 0; border-radius: 4px; cursor: pointer; font: 700 9.5px/1 "Segoe UI", Inter, sans-serif; letter-spacing: 2px; color: #4a3b28; text-shadow: 0 1px 0 rgba(255,245,225,0.8);
    background: linear-gradient(180deg, #faf4ec 0%, #e2d7c6 45%, #d1c4b0 55%, #bcae98 100%); box-shadow: 0 0 0 1.5px #3b3022, 0 0 0 3px rgba(0,0,0,0.2), 0 2px 3px rgba(0,0,0,0.35), inset 0 1px 0 #fff8ec;
    opacity: 0; translate: 0 3px; pointer-events: none; transition: opacity 250ms, translate 250ms; }
  .update-key.shown { opacity: 1; translate: 0 0; pointer-events: auto; }
  .update-key:active { translate: 0 1px; box-shadow: 0 0 0 1.5px #3b3022, 0 0 0 3px rgba(0,0,0,0.2), inset 0 2px 3px rgba(0,0,0,0.25); }
  .display.clickable { cursor: pointer; }
  /* Meter area: no surround of its own, the meters draw their own cut-outs in the plate */
  .meter-window { position: absolute; left: var(--pad); top: 34px; width: 490px; height: 190px; display: grid; place-items: center; }
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
  .knob-well { position: absolute; right: 48px; top: 50%; translate: 0 -54%; width: 260px; height: 260px; }
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
  .volume-label { position: absolute; right: 48px; width: 260px; text-align: center; bottom: 26px; font: 600 9.5px/1 "Segoe UI", Inter, sans-serif; letter-spacing: 3px; color: #5a4934; text-shadow: 0 1px 0 rgba(255,245,225,0.7); pointer-events: none; }
  .mode-switch { position: absolute; top: 9px; right: 3px; width: 54px; height: 84px; border: 0; padding: 0; background: none; cursor: pointer; display: flex; flex-direction: column; align-items: center; justify-content: space-between; }
  .engraving { font: 700 7.5px/1 "Segoe UI", Inter, sans-serif; letter-spacing: 1.4px; margin-right: -1.4px; color: #4a3b28; text-shadow: 0 1px 0 rgba(255,245,225,0.75); }
  .switch-body { position: relative; width: 26px; height: 26px; }
  .nut { position: absolute; inset: 0; clip-path: polygon(25% 3%, 75% 3%, 100% 50%, 75% 97%, 25% 97%, 0 50%); background: linear-gradient(160deg, #fbfbf8 0%, #c9c6bf 38%, #8e8a82 62%, #dcd9d2 100%); filter: drop-shadow(0 2px 2px rgba(0,0,0,0.45)); }
  .collar { position: absolute; inset: 6px; border-radius: 50%; background: radial-gradient(circle at 40% 35%, #ffffff 0%, #bdb9b1 45%, #5f5b54 100%); box-shadow: 0 0 0 1px rgba(40,34,26,0.55), inset 0 -1px 2px rgba(0,0,0,0.4); }
  .collar::after { content: ''; position: absolute; inset: 4px; border-radius: 50%; background: radial-gradient(circle at 50% 50%, #1d1a16 0%, #4a463f 100%); }
  /* Lever pivots at the collar centre; the throw is a fold through the viewer (scaleY 1 -> -1), like a real bat handle seen face-on */
  .lever { position: absolute; left: 50%; bottom: 50%; width: 9px; height: 23px; margin-left: -4.5px; transform-origin: 50% 100%; transition: transform 0.22s cubic-bezier(0.55, 0, 0.75, 1.4); filter: drop-shadow(1px 2px 1.5px rgba(0,0,0,0.45)); }
  .lever-shaft { position: absolute; left: 2px; right: 2px; top: 3px; bottom: 0; background: linear-gradient(90deg, #5d5a54 0%, #d9d6cf 30%, #ffffff 45%, #a7a39b 70%, #4a4741 100%); clip-path: polygon(0 0, 100% 0, 80% 100%, 20% 100%); }
  .lever-tip { position: absolute; left: 0; top: -3px; width: 9px; height: 9px; border-radius: 50%; background: radial-gradient(circle at 38% 32%, #ffffff 0%, #d4d1ca 35%, #7c7870 80%, #4f4b45 100%); }
  .mode-switch.thrown .lever { transform: scaleY(-1); }
  .mode-switch.thrown .lever-tip { background: radial-gradient(circle at 38% 68%, #ffffff 0%, #d4d1ca 35%, #7c7870 80%, #4f4b45 100%); }
  .mode-switch:hover .lever { transform: scaleY(0.9); } .mode-switch.thrown:hover .lever { transform: scaleY(-1); }
  .mode-switch:focus-visible { outline: 2px solid rgba(74, 59, 40, 0.6); outline-offset: 2px; border-radius: 6px; }
</style>
