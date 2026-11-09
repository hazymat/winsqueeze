import { app } from '../state.svelte'
// Following the song (SPEC.md › Lyrics view): where playback is now, from the last status plus the time since it arrived
export type TimedLine = { t: number; text: string }
const leadSeconds = 0.15 // a line lights just before it's sung
export function playPosition(now: number) { const status = app.nowPlaying; return Number(status?.time ?? 0) + (status?.mode === 'play' ? (now - app.receivedAt) / 1000 : 0) + leadSeconds }
// The line being sung: the last one that has started (-1 before the first)
export function lineAt(lines: TimedLine[], seconds: number) { let found = -1; for (let index = 0; index < lines.length && lines[index].t <= seconds; index++) found = index; return found }
// Plain lyrics as verses: blank lines separate them
export const versesOf = (text: string) => String(text ?? '').split(/\n\s*\n/).map(verse => verse.trim()).filter(Boolean).map(verse => verse.split('\n'))
// Set like a book: curly quotes and apostrophes (’cause, ’79 and rock ’n’ roll elide), proper dashes and ellipses
export const bookText = (text: string) => String(text ?? '').replace(/(^|[\s(\[{\u2014-])"/g, '$1\u201c').replace(/"/g, '\u201d').replace(/(^|[\s(\[{\u2014-])'(?=\d|(?:cause|cos|em|til|n|bout|round|tis|twas|fore)\b)/gi, '$1\u2019').replace(/(^|[\s(\[{\u2014-])'/g, '$1\u2018').replace(/'/g, '\u2019').replace(/\s--?\s/g, ' \u2014 ').replace(/\.\.\./g, '\u2026')
