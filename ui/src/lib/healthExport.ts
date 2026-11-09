import { t, tn } from './i18n'
// Library health › Export (SPEC.md › Library health): every problem as a flat list with server file paths, as JSON or plain text, for a script or an AI to work through
const problemTypes = (): Record<string, string> => ({
  'missing-cover-art': t('No cover image in the album folder or embedded in the files.'),
  'missing-composer': t('Tracks with no composer tag.'),
  'duplicate-album': t('The same album in more than one folder: identical track lists, or the same artist and title with at least half the tracks in common.'),
  'album-artist': t('No artist at all, or the album artist tag is missing on some tracks, differs between tracks, or is missing on an album whose tracks have different artists.'),
  'split-album': t('Tracks in one folder have different album, album artist or year tags, so the server shows them as more than one album.'),
  'track-numbers': t('Track number tags missing, repeated or skipped (per disc).'),
  'year': t('Year tag missing, odd (before 1900 or in the future) or different between tracks.'),
  'split-name': t('Two names that only ever appear together, probably one name the server split at a separator, such as “Earth” and “Wind & Fire”.'),
  'spelled-twice': t('One person spelled more than one way, such as “Bach, Johann Sebastian” and “Johann Sebastian Bach”.'),
  'damaged-file': t('Audio frames failed to decode while playing. Restore the file from a backup or re-rip it.')
})
const tagTypes: Record<string, string> = { artist: 'album-artist', split: 'split-album', numbers: 'track-numbers', year: 'year' }

export function healthProblems(report: any, damagedFiles: any[]): any[] {
  const album = (type: string, row: any, detail: string, files?: string[]) => Object.assign({ type, artist: row.artist, album: row.title, folder: row.path, detail }, files?.length ? { files } : {})
  const problems: any[] = []
  for (const row of report.missingArt ?? []) problems.push(album('missing-cover-art', row, ''))
  for (const row of report.missingComposer ?? []) problems.push(Object.assign(album('missing-composer', row, row.detail, row.files), { classical: !!row.classical }))
  for (const group of report.duplicates ?? []) problems.push({ type: 'duplicate-album', detail: group.reason, albums: group.albums.map((row: any) => ({ artist: row.artist, album: row.title, folder: row.path, tracks: row.tracks, format: row.format })) })
  for (const row of report.tags ?? []) for (const problem of row.problems) problems.push(album(tagTypes[problem.kind] ?? problem.kind, row, problem.text, problem.files))
  for (const row of report.splitNames ?? []) problems.push({ type: 'split-name', role: row.role, names: row.names, tracks: row.tracks, album: row.title, folder: row.path })
  for (const group of report.spelledTwice ?? []) problems.push({ type: 'spelled-twice', role: group.role, names: group.people.map((person: any) => ({ name: person.name, albums: person.albums })) })
  for (const file of damagedFiles) problems.push({ type: 'damaged-file', artist: file.artist, album: file.album, file: file.path, detail: file.unplayable ? 'unplayable' : `${file.errors} errors${file.firstError ? ': ' + file.firstError : ''}` })
  return problems
}

export function healthExportJson(report: any, damagedFiles: any[]): string {
  const problems = healthProblems(report, damagedFiles)
  return JSON.stringify({ exported: new Date().toISOString(), albums: report.albums, tracks: report.tracks, paths: t('File paths are as the server sees them.'), problemTypes: problemTypes(), problems }, null, 2)
}

export function healthExportText(report: any, damagedFiles: any[]): string {
  const problems = healthProblems(report, damagedFiles), types = problemTypes(), lines: string[] = []
  lines.push(`WinSqueeze · ${t('Library health')} · ${new Date().toLocaleString()}`, `${tn(problems.length, '{count} problem', '{count} problems')}. ${t('File paths are as the server sees them.')}`, '', t('Problem types'))
  for (const type in types) lines.push(`  ${type}: ${types[type]}`)
  for (const problem of problems) {
    lines.push('')
    if (problem.type === 'duplicate-album') { lines.push(`[${problem.type}] ${problem.detail}`); for (const row of problem.albums) lines.push(`  ${row.folder}  (${row.artist} — ${row.album}, ${tn(row.tracks, '{count} track', '{count} tracks')}, ${row.format})`); continue }
    if (problem.type === 'split-name') { lines.push(`[${problem.type}] ${problem.role}: “${problem.names[0]}” + “${problem.names[1]}” (${tn(problem.tracks, '{count} track', '{count} tracks')})`); if (problem.folder) lines.push(`  ${problem.folder}`); continue }
    if (problem.type === 'spelled-twice') { lines.push(`[${problem.type}] ${problem.role}: ${problem.names.map((person: any) => `${person.name} (${person.albums})`).join(' / ')}`); continue }
    lines.push(`[${problem.type}] ${[problem.artist, problem.album, problem.detail].filter(Boolean).join(' — ')}`, `  ${problem.file ?? problem.folder}`)
    for (const file of problem.files ?? []) lines.push(`    ${file}`)
  }
  return lines.join('\r\n') + '\r\n'
}
