<script lang="ts">
  import { query } from '../bridge'
  import { ui, formatTime } from '../state.svelte'
  // Info for one or several tracks: total length, what they all share, and a per-track table for whatever differs
  let { ids }: { ids: number[] } = $props()
  let tracks = $state<any[] | null>(null)
  $effect(() => { const wanted = ids; tracks = null; query('trackInfo', { ids: wanted }).then(rows => { const byId = new Map((Array.isArray(rows) ? rows : []).map((row: any) => [Number(row.id), row])); tracks = wanted.map(id => byId.get(id)).filter(Boolean) }) })
  const folderOf = (url: string) => { try { const path = decodeURIComponent(String(url ?? '').replace(/^file:\/\/\/?/, '')).replace(/\//g, '\\'); return path.slice(0, path.lastIndexOf('\\')) } catch { return '' } }
  const formats: Record<string, string> = { flc: 'FLAC', mp3: 'MP3', aac: 'AAC', mp4: 'AAC', alc: 'ALAC', wav: 'WAV', aif: 'AIFF', ogg: 'Ogg Vorbis', ops: 'Opus', wma: 'WMA', dsf: 'DSD', dff: 'DSD' }
  const fields: [string, string, (track: any) => string][] = [
    ['title', 'Title', track => track.title ?? ''], ['artist', 'Artist', track => track.track_artist ?? ''], ['albumArtist', 'Album artist', track => track.album_artist ?? ''], ['album', 'Album', track => track.album ?? ''],
    ['composer', 'Composer', track => track.composer ?? ''], ['genre', 'Genre', track => track.genre ?? ''], ['year', 'Year', track => Number(track.year) > 0 ? String(track.year) : ''],
    ['disc', 'Disc', track => Number(track.disc_no) > 0 ? String(track.disc_no) : ''], ['number', 'Track', track => Number(track.track_no) > 0 ? String(track.track_no) : ''],
    ['format', 'Format', track => formats[track.content_type] ?? String(track.content_type ?? '').toUpperCase()], ['rate', 'Sample rate', track => Number(track.sample_rate) > 0 ? `${Number(track.sample_rate) / 1000} kHz` : ''],
    ['depth', 'Bit depth', track => Number(track.sample_size) > 0 ? `${track.sample_size}-bit` : ''], ['bitrate', 'Bitrate', track => String(track.bitrate ?? '').replace(/kb\/s/i, ' kbps').replace(/\s+/g, ' ').trim()],
    ['folder', 'Folder', track => folderOf(track.url)]
  ]
  const totalSeconds = $derived((tracks ?? []).reduce((total, track) => total + Number(track.duration || 0), 0))
  const shared = $derived((tracks ?? []).length ? fields.map(([key, label, read]) => ({ key, label, value: read(tracks![0]), same: tracks!.every(track => read(track) === read(tracks![0])) })).filter(field => field.same && field.value) : [])
  const differing = $derived((tracks ?? []).length > 1 ? fields.filter(([key, label, read]) => !tracks!.every(track => read(track) === read(tracks![0])) && tracks!.some(track => read(track))) : [])
</script>

<div class="scrim" role="presentation" onclick={() => ui.trackInfo = null}></div>
<div class="track-info" role="dialog" aria-label="Track info">
  <header><div><h3>{ids.length === 1 ? 'Track info' : `${ids.length} tracks`}</h3>{#if tracks}<span class="quiet">{formatTime(totalSeconds)} total{tracks.length < ids.length ? ` · ${ids.length - tracks.length} not in the library index` : ''}</span>{/if}</div><button class="dismiss" onclick={() => ui.trackInfo = null} aria-label="Close" title="Close (Esc)">×</button></header>
  <div class="body">
    {#if !tracks}<p class="quiet">Loading…</p>
    {:else if !tracks.length}<p class="quiet">These tracks aren't in the library index.</p>
    {:else}
      {#if shared.length}
        {#if tracks.length > 1}<h4>Shared by all</h4>{/if}
        <dl>{#each shared as field (field.key)}<dt>{field.label}</dt><dd class:path={field.key === 'folder'}>{field.value}</dd>{/each}{#if tracks.length === 1}<dt>Length</dt><dd>{formatTime(totalSeconds)}</dd>{/if}</dl>
      {/if}
      {#if differing.length}
        <h4>Per track</h4>
        <div class="table-wrap"><table>
          <thead><tr>{#each differing as [key, label] (key)}<th>{label}</th>{/each}<th class="time">Length</th></tr></thead>
          <tbody>{#each tracks as track (track.id)}<tr>{#each differing as [key, label, read] (key)}<td class:path={key === 'folder'}>{read(track)}</td>{/each}<td class="time">{formatTime(Number(track.duration || 0))}</td></tr>{/each}</tbody>
        </table></div>
      {/if}
    {/if}
  </div>
</div>

<style>
  .scrim { position: fixed; inset: 0; z-index: 94; background: rgba(0, 0, 0, 0.25); }
  .track-info { position: fixed; z-index: 95; left: 50%; top: 50%; transform: translate(-50%, -50%); width: min(760px, calc(100vw - 36px)); max-height: calc(100vh - 80px); display: flex; flex-direction: column; border-radius: 14px; padding: 14px 16px; background: var(--panel-solid); backdrop-filter: blur(28px) saturate(1.4); border: 1px solid var(--line); box-shadow: 0 18px 50px var(--shadow); user-select: text; }
  header { display: flex; align-items: flex-start; justify-content: space-between; gap: 10px; }
  h3 { margin: 0; font-size: 16px; } h4 { margin: 14px 0 6px; color: var(--fg-quiet); font-size: 10.5px; text-transform: uppercase; letter-spacing: 0.06em; }
  .quiet { color: var(--fg-quiet); font-size: 12px; }
  .dismiss { background: none; border: 0; color: var(--fg-quiet); font-size: 20px; line-height: 1; cursor: pointer; padding: 0 2px; } .dismiss:hover { color: var(--fg); }
  .body { overflow-y: auto; margin-top: 6px; }
  dl { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px; margin: 0; font-size: 13px; } dt { color: var(--fg-quiet); } dd { margin: 0; overflow-wrap: anywhere; }
  .path { font-size: 12px; color: var(--fg-quiet); }
  .table-wrap { overflow-x: auto; }
  table { width: 100%; border-collapse: collapse; font-size: 12.5px; } th { text-align: left; color: var(--fg-quiet); font-weight: 600; padding: 4px 10px 4px 0; border-bottom: 1px solid var(--line); white-space: nowrap; }
  td { padding: 4px 10px 4px 0; border-bottom: 1px solid color-mix(in srgb, var(--line) 50%, transparent); vertical-align: top; } .time { text-align: right; padding-right: 0; font-variant-numeric: tabular-nums; }
</style>
