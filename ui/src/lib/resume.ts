import { command } from '../bridge'
import { app, toast, formatTime, readSetting, writeSetting } from '../state.svelte'
import { t } from './i18n'
// Long tracks (20 min and up: symphonies, live sets) remember where you left them and carry on from there next time
const longSeconds = 20 * 60, keep = 50
const trackOf = (status: any) => status?.playlist_loop?.find((item: any) => String(item['playlist index']) === String(status?.playlist_cur_index))
const load = (): [string, number][] => { try { const saved = JSON.parse(readSetting('resume', '[]')); return Array.isArray(saved) ? saved : [] } catch { return [] } } // oldest first
let lastPlayer = ''
export function noteLongTrack(previous: any, next: any) {
  const samePlayer = lastPlayer === app.selectedPlayer; lastPlayer = app.selectedPlayer
  if (!samePlayer) return
  const left = trackOf(previous), arrived = trackOf(next)
  if (!left || !arrived || String(left.id) === String(arrived.id) || !(Number(arrived.id) > 0)) return
  const spots = load(), leftDuration = Number(left.duration ?? 0), leftAt = Number(previous.time ?? 0)
  const spotsNow = spots.filter(([id]) => id !== String(left.id))
  if (Number(left.id) > 0 && leftDuration >= longSeconds && leftAt > 60 && leftDuration - leftAt > 60) spotsNow.push([String(left.id), Math.floor(leftAt)])
  const resumeAt = spotsNow.find(([id]) => id === String(arrived.id))?.[1]
  if (resumeAt && Number(arrived.duration ?? 0) >= longSeconds && Number(next.time ?? 0) < 10) {
    command('time', resumeAt)
    toast(t('Carrying on from {time} where you left “{title}”', { time: formatTime(resumeAt), title: arrived.title }), () => command('time', 0), 6000, { keep: false })
  }
  writeSetting('resume', JSON.stringify(spotsNow.slice(-keep)))
}
