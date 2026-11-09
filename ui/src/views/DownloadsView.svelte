<script lang="ts">
  import { t, tn } from '../lib/i18n'
  import { artUrl, sendToShell } from '../bridge'
  import { app } from '../state.svelte'
  import { downloads, cancelDownloads, formatBytes } from '../lib/downloads.svelte'
  import AlbumsOf from './AlbumsOf.svelte'
  import PageHead from '../parts/PageHead.svelte'
  // Downloaded (SPEC.md › Offline downloads): what's still coming in sits on top (expand an album for its tracks, ✕ cancels), then the downloaded albums as a normal album grid
  let expanded = $state<string[]>([])
  const toggle = (key: string) => expanded = expanded.includes(key) ? expanded.filter(open => open !== key) : expanded.concat([key])
  const totals = $derived(downloads.active.reduce((sum, group) => ({ tracks: sum.tracks + group.total - group.done, failed: sum.failed + group.failed }), { tracks: 0, failed: 0 }))
  const percent = (received: number, size: number) => size > 0 ? Math.min(100, (received / size) * 100) : 0
  const groupPercent = (group: any) => group.total ? ((group.done + group.tracks.filter((track: any) => track.state === 'downloading').reduce((sum: number, track: any) => sum + percent(track.received, track.size) / 100, 0)) / group.total) * 100 : 0
  const trackState = (track: any) => track.state === 'done' ? t('Done') : track.state === 'downloading' ? (track.size ? `${Math.round(percent(track.received, track.size))}%` : t('Starting')) : track.state === 'failed' ? t('Failed') : t('Waiting')
</script>

{#if app.status?.offline}
  <div class="banner"><span>{app.status.serverBack ? t('The server is back.') : t('Offline: playing what\'s on this PC.')}</span><button class="primary" onclick={() => sendToShell({ type: 'goOnline' })}>{t('Go online')}</button></div>
{/if}
{#if downloads.active.length}
  <section class="manager">
    <div class="manager-head">
      <strong>{t('Downloading')}</strong>
      <span class="quiet">{tn(totals.tracks, '{count} track to go', '{count} tracks to go')}{downloads.waiting ? ' · ' + t('waiting for the server') : ''}{downloads.problem ? ` · ${downloads.problem}` : ''}</span>
      {#if totals.failed}<button class="ghost" onclick={() => sendToShell({ type: 'downloadRetry' })}>{t('Retry failed')}</button>{/if}
      <button class="ghost" onclick={() => sendToShell({ type: 'downloadCancelAll' })}>{t('Cancel all')}</button>
    </div>
    {#each downloads.active as group (group.key)}
      <div class="group">
        <button class="group-row" onclick={() => toggle(group.key)} aria-expanded={expanded.includes(group.key)}>
          {#if group.cover}<img src={artUrl(group.cover, 80)} alt="" />{:else}<span class="no-art">♪</span>{/if}
          <span class="text"><span class="title">{group.title || 'Tracks'}</span><span class="quiet">{group.artist}</span></span>
          <span class="bar"><span style:width="{groupPercent(group)}%"></span></span>
          <span class="count quiet">{t('{done} of {total}', { done: group.done, total: group.total })}{group.failed ? ' · ' + t('{count} failed', { count: group.failed }) : ''}</span>
          <span class="chevron" class:open={expanded.includes(group.key)}>⌄</span>
        </button>
        <button class="cancel" onclick={() => cancelDownloads([group.key])} aria-label={t('Cancel this album\'s downloads')} title={t('Cancel the rest of this album')}>✕</button>
      </div>
      {#if expanded.includes(group.key)}
        <div class="tracks">
          {#each group.tracks as track (track.id)}
            <div class="track" class:failed={track.state === 'failed'}>
              <span class="track-title">{track.title}</span>
              {#if track.state === 'downloading'}<span class="bar small"><span style:width="{percent(track.received, track.size)}%"></span></span>{/if}
              <span class="quiet" title={track.error}>{trackState(track)}</span>
              {#if track.state !== 'done'}<button class="cancel" onclick={() => cancelDownloads([], [track.id])} aria-label={t('Cancel this track')} title={t('Cancel this track')}>✕</button>{:else}<span class="cancel-space"></span>{/if}
            </div>
          {/each}
        </div>
      {/if}
    {/each}
  </section>
{/if}
{#if downloads.finishedTracks}
  <AlbumsOf kind="downloads" title={t('Downloaded')} />
  <div class="footer quiet">{tn(downloads.finishedTracks, '{count} track on this PC', '{count} tracks on this PC')} · {formatBytes(downloads.totalBytes)} · <button class="link" onclick={() => sendToShell({ type: 'openDownloads' })}>{t('Open folder')}</button>{#if !app.status?.offline} · <button class="link" onclick={() => sendToShell({ type: 'goOffline' })}>{t('Play offline')}</button>{/if}</div>
{:else if !downloads.active.length}
  <PageHead title={t('Downloaded')} />
  <p class="quiet empty">{t('Nothing downloaded yet. Pick albums, artists or tracks (press and hold, or Ctrl+click) and choose Download, or use Download on an album. Downloaded music plays here even when the server can\'t be reached.')}</p>
{/if}

<style>
  .banner { display: flex; align-items: center; gap: 12px; margin: 8px 0 4px; padding: 10px 14px; border-radius: 10px; background: var(--accent-soft); color: var(--fg); }
  .banner span { flex: 1; }
  .manager { margin: 10px 0 6px; padding: 10px 12px; border-radius: 12px; background: var(--panel); border: 1px solid var(--line); }
  .manager-head { display: flex; align-items: center; gap: 10px; margin-bottom: 6px; }
  .manager-head .quiet { flex: 1; min-width: 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .quiet { color: var(--fg-quiet); font-size: 12px; }
  .group { display: flex; align-items: center; gap: 4px; }
  .group-row { flex: 1; min-width: 0; display: flex; align-items: center; gap: 10px; padding: 5px 6px; border: 0; border-radius: 8px; background: none; color: var(--fg); font: inherit; text-align: left; cursor: pointer; }
  .group-row:hover { background: var(--hover); }
  .group-row img, .no-art { width: 36px; height: 36px; border-radius: 5px; object-fit: cover; flex: none; }
  .no-art { display: grid; place-items: center; background: var(--panel-strong); color: var(--fg-quiet); }
  .text { flex: 1; min-width: 0; display: flex; flex-direction: column; }
  .title { font-weight: 600; font-size: 13px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .text .quiet { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .bar { flex: 0 0 120px; height: 5px; border-radius: 3px; background: var(--line); overflow: hidden; }
  .bar span { display: block; height: 100%; background: var(--accent); transition: width 300ms; }
  .bar.small { flex-basis: 70px; height: 4px; }
  .count { flex: none; min-width: 64px; text-align: right; }
  .chevron { flex: none; color: var(--fg-quiet); transition: transform 150ms; } .chevron.open { transform: rotate(180deg); }
  .cancel { flex: none; width: 26px; height: 26px; border: 0; border-radius: 50%; background: none; color: var(--fg-quiet); cursor: pointer; font-size: 12px; }
  .cancel:hover { background: var(--hover); color: var(--fg); }
  .cancel-space { width: 26px; flex: none; }
  .tracks { margin: 0 0 6px 52px; }
  .track { display: flex; align-items: center; gap: 10px; padding: 2px 0; font-size: 12.5px; }
  .track-title { flex: 1; min-width: 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .track.failed .quiet { color: var(--danger, #e5484d); }
  .footer { margin: 22px 0 8px; }
  .link { background: none; border: 0; padding: 0; color: var(--accent); font: inherit; cursor: pointer; }
  .empty { max-width: 520px; line-height: 1.5; }
</style>
