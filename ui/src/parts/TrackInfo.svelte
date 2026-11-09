<script lang="ts">
  import { t } from '../lib/i18n'
  import { query } from '../bridge'
  import { ui, formatTime } from '../state.svelte'
  // Info for one or several tracks: total length, what they all share, and a per-track table for whatever differs. Same look as Now playing › Album (section heads over hairline rows)
  // items: things outside the library (favourites, radio, episodes), shown as whatever fields the server gave
  let { ids, items = null }: { ids: number[], items?: { name: string, fields: [string, string][] }[] | null } = $props()
  let tracks = $state<any[] | null>(null)
  $effect(() => { const wanted = ids; tracks = null; if (items) return; query('trackInfo', { ids: wanted }).then(rows => { const byId = new Map((Array.isArray(rows) ? rows : []).map((row: any) => [Number(row.id), row])); tracks = wanted.map(id => byId.get(id)).filter(Boolean) }) })
  const pathOf = (url: string) => { try { return decodeURIComponent(String(url ?? '').replace(/^file:\/\/\/?/, '')).replace(/\//g, '\\') } catch { return '' } } // the full path to the file, as the server sees it
  const folderOf = (url: string) => pathOf(url).replace(/\\[^\\]*$/, ''), fileNameOf = (url: string) => pathOf(url).split('\\').pop() ?? ''
  const formats: Record<string, string> = { flc: 'FLAC', mp3: 'MP3', aac: 'AAC', mp4: 'AAC', alc: 'ALAC', wav: 'WAV', aif: 'AIFF', ogg: 'Ogg Vorbis', ops: 'Opus', wma: 'WMA', dsf: 'DSD', dff: 'DSD' }
  const formatOf = (track: any) => [formats[track.content_type] ?? String(track.content_type ?? '').toUpperCase(), Number(track.sample_rate) > 0 ? `${Number(track.sample_rate) / 1000} kHz` : '', Number(track.sample_size) > 0 ? `${track.sample_size}-bit` : '', String(track.bitrate ?? '').replace(/kb\/s/i, ' kbps').replace(/\s+/g, ' ').trim()].filter(Boolean).join(' · ') // one line, as on the Album tab
  const fields: [string, string, (track: any) => string][] = [
    ['title', t('Title'), track => track.title ?? ''], ['artist', t('Artist'), track => track.track_artist ?? ''], ['albumArtist', t('Album artist'), track => track.album_artist ?? ''], ['album', t('Album'), track => track.album ?? ''],
    ['composer', t('Composer'), track => track.composer ?? ''], ['genre', t('Genre'), track => track.genre ?? ''], ['year', t('Year'), track => Number(track.year) > 0 ? String(track.year) : ''],
    ['disc', t('Disc'), track => Number(track.disc_no) > 0 ? String(track.disc_no) : ''], ['number', t('Track'), track => Number(track.track_no) > 0 ? String(track.track_no) : ''],
    ['format', t('Format'), formatOf], ['folder', t('Folder'), track => folderOf(track.url)], ['file', t('File'), track => fileNameOf(track.url)]
  ]
  const sameIn = (read: (track: any) => string) => tracks!.every(track => read(track) === read(tracks![0]))
  const single = $derived(tracks?.length === 1), sharedAlbum = $derived(tracks?.length && !single && sameIn(track => track.album ?? '') ? tracks[0].album : '')
  const totalSeconds = $derived((tracks ?? []).reduce((total, track) => total + Number(track.duration || 0), 0))
  const heading = $derived(items ? (items.length === 1 ? items[0].name : t('{count} items', { count: items.length })) : single ? tracks![0].title : sharedAlbum || (ids.length === 1 ? t('Track info') : t('{count} tracks', { count: ids.length })))
  const subheading = $derived(items || !tracks ? '' : single ? tracks[0].track_artist ?? '' : [sharedAlbum ? t('{count} tracks', { count: tracks.length }) : '', t('{time} total', { time: formatTime(totalSeconds) }), tracks.length < ids.length ? t('{count} not in the library index', { count: ids.length - tracks.length }) : ''].filter(Boolean).join(' · '))
  const hidden = $derived(single ? ['title', 'artist'] : sharedAlbum ? ['album'] : []) // already in the heading
  const shared = $derived((tracks ?? []).length ? fields.filter(([key, label, read]) => !hidden.includes(key) && read(tracks![0]) && sameIn(read)).map(([key, label, read]) => [label, read(tracks![0])]).concat(single ? [['Length', formatTime(totalSeconds)]] : []) : [])
  const differing = $derived((tracks ?? []).length > 1 ? fields.filter(([key, label, read]) => !sameIn(read) && tracks!.some(track => read(track))) : [])
</script>

<div class="scrim" role="presentation" onclick={() => { ui.trackInfo = null; ui.itemInfo = null }}></div>
<div class="track-info" role="dialog" aria-label={t('Track info')}>
  <header><div><h2>{heading}</h2>{#if subheading}<div class="quiet">{subheading}</div>{/if}</div><button class="dismiss" onclick={() => { ui.trackInfo = null; ui.itemInfo = null }} aria-label={t('Close')} title={t('Close (Esc)')}>×</button></header>
  <div class="body">
    {#if items}
      {#each items as item}{#if items.length > 1}<div class="section-head">{item.name}</div>{/if}<dl class="tag-table">{#each item.fields as [label, value]}<dt>{label}</dt><dd class:path={label === t('Address')}>{value}</dd>{/each}</dl>{/each}
    {:else if !tracks}<p class="quiet">{t('Loading…')}</p>
    {:else if !tracks.length}<p class="quiet">{t('These tracks aren\'t in the library index.')}</p>
    {:else}
      {#if shared.length}
        <div class="section-head">{single ? t('From your music file') : t('Shared by all')}</div>
        <dl class="tag-table">{#each shared as [label, value] (label)}<dt>{label}</dt><dd class:path={label === t('Folder')}>{value}</dd>{/each}</dl>
      {/if}
      {#if differing.length}
        <div class="section-head">{t('Per track')}</div>
        <div class="table-wrap"><table>
          <thead><tr>{#each differing as [key, label] (key)}<th>{label}</th>{/each}<th class="time">{t('Length')}</th></tr></thead>
          <tbody>{#each tracks as track (track.id)}<tr>{#each differing as [key, label, read] (key)}<td class:path={key === 'folder'} class:number={key === 'number' || key === 'disc'}>{read(track)}</td>{/each}<td class="time">{formatTime(Number(track.duration || 0))}</td></tr>{/each}</tbody>
        </table></div>
      {/if}
    {/if}
  </div>
</div>

<style>
  .scrim { position: fixed; inset: 0; z-index: 94; background: rgba(0, 0, 0, 0.25); }
  .track-info { position: fixed; z-index: 95; left: 50%; top: 50%; transform: translate(-50%, -50%); width: min(760px, calc(100vw - 36px)); max-height: calc(100vh - 80px); display: flex; flex-direction: column; box-sizing: border-box; border-radius: 14px; padding: 18px 20px 20px; background: var(--panel-solid); backdrop-filter: blur(28px) saturate(1.4); border: 1px solid var(--line); box-shadow: 0 18px 50px var(--shadow); user-select: text; }
  header { display: flex; align-items: flex-start; justify-content: space-between; gap: 10px; }
  h2 { margin: 0 0 2px; font-size: 18px; } header .quiet { font-size: 12.5px; }
  .dismiss { background: none; border: 0; color: var(--fg-quiet); font-size: 20px; line-height: 1; cursor: pointer; padding: 0 2px; } .dismiss:hover { color: var(--fg); }
  .body { overflow-y: auto; margin-top: 2px; } .body > .quiet { font-size: 13px; }
  .body :global(.tag-table) { grid-template-columns: 130px 1fr; } /* labels are short here; the Album tab's wider share would leave a gap */
  .path { font-size: 12px; color: var(--fg-quiet); }
  .table-wrap { overflow-x: auto; }
  table { width: 100%; border-collapse: collapse; font-size: 13px; line-height: 1.4; border-top: 1px solid var(--line); } th { text-align: left; color: var(--fg-quiet); font-weight: 400; padding: 6px 12px 6px 4px; border-bottom: 1px solid var(--line); white-space: nowrap; }
  td { padding: 6px 12px 6px 4px; border-bottom: 1px solid var(--line); vertical-align: top; } .number, .time { font-variant-numeric: tabular-nums; } .time { text-align: right; padding-right: 4px; }
</style>
