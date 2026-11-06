import { app, currentTrack } from '../state.svelte'

// Bit-perfect check for this PC's player (SPEC.md › Playback and fidelity): lossless source at its native rate, exclusive output, volume at 100, EQ bypassed
export function fidelity() {
  const track = currentTrack(), status = app.status
  const ownPlayer = !app.selectedPlayer || app.selectedPlayer === app.ownPlayer
  if (!track || !ownPlayer || status?.state !== 'Running') return { state: '', reasons: [] as string[] }
  const codec = String(track.type ?? '').toUpperCase()
  const sourceRate = Number(track.samplerate ?? 0), outputRate = Number(status.sampleRate ?? 0)
  const volume = Math.abs(Number(app.nowPlaying?.['mixer volume'] ?? 100))
  const reasons: string[] = []
  if (!['FLC', 'FLAC', 'ALC', 'ALAC', 'WAV', 'AIF', 'AIFF', 'PCM'].includes(codec)) reasons.push('the source is lossy')
  if (!status.exclusive) reasons.push('shared mode goes through the Windows mixer')
  else if (sourceRate > 0 && outputRate > 0 && sourceRate !== outputRate) reasons.push(`the device is running at a different rate (resampled)`)
  if (status.eqActive) reasons.push('the equaliser is on')
  return { state: volume < 100 ? 'scaled' : reasons.length ? 'blocked' : 'perfect', reasons }
}
