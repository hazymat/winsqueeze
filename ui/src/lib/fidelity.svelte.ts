import { app, currentTrack } from '../state.svelte'
import { t } from './i18n'

// Bit-perfect check for this PC's player (SPEC.md › Playback and fidelity): decoded samples reach the DAC untouched (native rate, exclusive, volume 100, EQ bypassed); lossy sources count, flagged in the tooltip
export function fidelity() {
  const track = currentTrack(), status = app.status
  const ownPlayer = !app.selectedPlayer || app.selectedPlayer === app.ownPlayer
  if (!track || !ownPlayer || status?.state !== 'Running') return { lossy: false, format: '', state: '', reasons: [] as string[] }
  const codec = String(track.type ?? '').toUpperCase()
  const sourceRate = Number(track.samplerate ?? 0), outputRate = Number(status.sampleRate ?? 0)
  const volume = Math.abs(Number(app.nowPlaying?.['mixer volume'] ?? 100))
  const reasons: string[] = []
  // Each reason stands alone (shown as its own bullet)
  if (!status.exclusive) reasons.push(t('Shared mode goes through the Windows mixer'))
  else if (sourceRate > 0 && outputRate > 0 && sourceRate !== outputRate) reasons.push(t('The device runs at a different sample rate (resampled)'))
  if (status.eqActive) reasons.push(t('The equaliser is on'))
  if (status.replayGain) reasons.push(t('ReplayGain is on (Settings › Playback)'))
  if (status.transitions) reasons.push(t('Crossfade or fades are on (Settings › Playback)'))
  return { format: codec.replace('FLC', 'FLAC').replace('ALC', 'ALAC'), lossy: !['FLC', 'FLAC', 'ALC', 'ALAC', 'WAV', 'AIF', 'AIFF', 'PCM'].includes(codec), state: volume < 100 ? 'scaled' : reasons.length ? 'blocked' : 'perfect', reasons }
}
