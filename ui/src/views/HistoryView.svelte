<script lang="ts">
  import { query, artUrl, sendToShell, command } from '../bridge'
  import { toast, ui } from '../state.svelte'
  import { selectable } from '../lib/selectable.svelte'
  import { queueTracks, resolveTracks } from '../lib/browseActions'
  import { history } from '../lib/history.svelte'
  import type { SelectionAction } from '../lib/selection.svelte'
  type Play = { id: number, played_at: number, player: string, track_id: number | null, url: string, title: string, artist: string, album: string, cover_id: string, artwork_url: string, station: string, remote: number, duration: number }
  type Block = { kind: 'album' | 'radio' | 'hour', start: number, end: number, plays: Play[] }
  const pageSize = 400
  let plays = $state<Play[]>([]), loading = $state(true), more = $state(false), confirmClear = $state(false)
  $effect(() => { history.version; query('history', {}).then(result => { plays = Array.isArray(result) ? result : []; more = plays.length === pageSize; loading = false }) })
  async function loadMore() { const older = await query('history', { before: plays[plays.length - 1].played_at }); if (Array.isArray(older)) { plays = plays.concat(older); more = older.length === pageSize } }
  // Days, newest first; within a day: an album played through (3+ tracks in a row) is one block, each radio programme is its own, the rest group by hour
  const dayKey = (seconds: number) => new Date(seconds * 1000).toDateString()
  const days = $derived.by(() => {
    const byDay = new Map<string, Play[]>()
    for (const play of plays) byDay.set(dayKey(play.played_at), (byDay.get(dayKey(play.played_at)) ?? []).concat([play]))
    return Array.from(byDay.entries()).map(([key, dayPlays]) => ({ key, label: dayLabel(dayPlays[0].played_at), blocks: blocksOf(dayPlays.slice().reverse()).reverse() }))
  })
  const sameAlbum = (first: Play, second: Play) => !first.remote && !second.remote && first.album !== '' && first.album === second.album && first.cover_id === second.cover_id && second.played_at - first.played_at < 2700
  function blocksOf(chronological: Play[]): Block[] {
    const blocks: Block[] = []
    for (let index = 0; index < chronological.length;) {
      const play = chronological[index]
      let runEnd = index + 1
      while (runEnd < chronological.length && sameAlbum(chronological[runEnd - 1], chronological[runEnd])) runEnd++
      if (runEnd - index >= 3) { blocks.push({ kind: 'album', start: play.played_at, end: chronological[runEnd - 1].played_at, plays: chronological.slice(index, runEnd).reverse() }); index = runEnd; continue }
      if (play.remote) { blocks.push({ kind: 'radio', start: play.played_at, end: play.played_at, plays: [play] }); index++; continue }
      const hour = new Date(play.played_at * 1000).getHours(), last = blocks[blocks.length - 1]
      if (last?.kind === 'hour' && new Date(last.start * 1000).getHours() === hour) { last.plays.unshift(play); last.end = play.played_at } else blocks.push({ kind: 'hour', start: play.played_at, end: play.played_at, plays: [play] })
      index++
    }
    return blocks
  }
  function dayLabel(seconds: number) {
    const date = new Date(seconds * 1000), today = new Date(); today.setHours(0, 0, 0, 0)
    const daysAgo = Math.round((today.getTime() - new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime()) / 86400000)
    return daysAgo === 0 ? 'Today' : daysAgo === 1 ? 'Yesterday' : date.toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long', year: date.getFullYear() === today.getFullYear() ? undefined : 'numeric' })
  }
  const clock = (seconds: number) => new Date(seconds * 1000).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })
  const hourLabel = (seconds: number) => { const hour = new Date(seconds * 1000); hour.setMinutes(0, 0, 0); const next = new Date(hour.getTime() + 3600000); return `${clock(hour.getTime() / 1000)}–${clock(next.getTime() / 1000)}` }
  const playOf = (key: string) => plays.find(play => String(play.id) === key)
  const trackIdsOf = (keys: string[]) => keys.map(playOf).filter(play => play?.track_id).map(play => String(play!.track_id))
  const playStream = (play: Play) => { if (play.url) command('playlist', 'play', play.url); else toast('That stream can’t be played again') }
  const playTracks = async (keys: string[], mode: 'load' | 'insert' | 'add') => { const streams = keys.map(playOf).filter(play => play?.remote); if (!trackIdsOf(keys).length && streams.length) { playStream(streams[0]!); return } queueTracks(await resolveTracks('track', trackIdsOf(keys)), mode) }
  const actions: SelectionAction[] = [
    { label: 'Play now', key: 'Enter', run: keys => playTracks(keys, 'load') },
    { label: 'Play next', run: keys => playTracks(keys, 'insert') },
    { label: 'Add to end', run: keys => playTracks(keys, 'add') },
    { label: 'Track info', run: keys => { ui.trackInfo = trackIdsOf(keys).map(Number) } },
    { label: 'Remove from history', danger: true, run: keys => sendToShell({ type: 'historyDelete', ids: keys.map(Number) }) }
  ]
  const art = (play: Play) => play.cover_id ? artUrl(play.cover_id, 120) : play.artwork_url
</script>

<div class="history-head">
  <h1>History</h1>
  {#if plays.length}{#if confirmClear}<span class="quiet">Clear all play history? This can’t be undone.</span><button class="danger" onclick={() => { sendToShell({ type: 'historyClear' }); confirmClear = false }}>Clear</button><button class="ghost" onclick={() => confirmClear = false}>Keep</button>{:else}<button class="ghost" onclick={() => confirmClear = true}>Clear history…</button>{/if}{/if}
</div>
{#if !loading && !plays.length}<p class="quiet">Nothing yet. Everything you play here (albums, tracks, radio programmes) is listed by day once it has played for 20 seconds.</p>{/if}
<div use:selectable={{ scope: 'history', noun: 'play', actions }}>
  {#each days as day (day.key)}
    <h2 class="day">{day.label}</h2>
    {#each day.blocks as block (block.plays[0].id)}
      {#if block.kind === 'album'}
        {@const first = block.plays[block.plays.length - 1]}
        <section class="block album-block">
          <button class="album-head" onclick={() => playTracks(block.plays.slice().reverse().map(play => String(play.id)), 'load')} title="Play these again">
            {#if art(first)}<img src={art(first)} alt="" />{:else}<span class="no-art">♪</span>{/if}
            <span class="text"><span class="title">{first.album}</span><span class="quiet">{first.artist} · {block.plays.length} tracks · {clock(block.start)}–{clock(block.end)}</span></span>
          </button>
          {#each block.plays as play (play.id)}<button class="play-row" data-select-key={String(play.id)} ondblclick={() => playTracks([String(play.id)], 'load')}><span class="time">{clock(play.played_at)}</span><span class="name">{play.title}</span><span class="quiet artist">{play.artist}</span></button>{/each}
        </section>
      {:else if block.kind === 'radio'}
        {@const play = block.plays[0]}
        <section class="block">
          <button class="play-row radio" data-select-key={String(play.id)} ondblclick={() => playStream(play)}>
            <span class="time">{clock(play.played_at)}</span>
            {#if art(play)}<img src={art(play)} alt="" />{:else}<span class="no-art small">📻</span>{/if}
            <span class="text"><span class="name">{play.station || play.title}</span><span class="quiet">{play.station && play.title !== play.station ? play.title : play.artist}</span></span>
            <span class="replay" role="button" tabindex="-1" onclick={(event) => { event.stopPropagation(); playStream(play) }} onkeydown={() => {}}>Play again</span>
          </button>
        </section>
      {:else}
        <section class="block">
          <h3 class="hour">{hourLabel(block.start)}</h3>
          {#each block.plays as play (play.id)}<button class="play-row" data-select-key={String(play.id)} ondblclick={() => playTracks([String(play.id)], 'load')}><span class="time">{clock(play.played_at)}</span><span class="name">{play.title}</span><span class="quiet artist">{play.artist}{play.album ? ' · ' + play.album : ''}</span></button>{/each}
        </section>
      {/if}
    {/each}
  {/each}
  {#if more}<button class="ghost load-more" onclick={loadMore}>Show older</button>{/if}
</div>

<style>
  .history-head { display: flex; align-items: center; gap: 10px; margin-bottom: 6px; } .history-head h1 { margin: 0 auto 0 0; font-size: 22px; }
  .day { font-size: 15px; font-weight: 650; margin: 22px 0 8px; position: sticky; top: 0; z-index: 2; padding: 6px 0; background: var(--bg-sticky, var(--bg)); }
  .hour { font-size: 12px; font-weight: 600; color: var(--fg-quiet); margin: 10px 0 4px; letter-spacing: 0.02em; }
  .block { margin-bottom: 6px; }
  .album-block { padding: 8px; border-radius: 10px; background: color-mix(in srgb, var(--fg) 4%, transparent); }
  .album-head { display: flex; align-items: center; gap: 12px; background: none; border: 0; color: var(--fg); cursor: pointer; padding: 0 0 6px; font: inherit; text-align: left; width: 100%; }
  .album-head img, .no-art { width: 48px; height: 48px; border-radius: 6px; object-fit: cover; display: grid; place-items: center; background: var(--panel-strong); flex-shrink: 0; }
  .text { display: flex; flex-direction: column; min-width: 0; } .title { font-weight: 600; }
  .play-row { display: flex; align-items: center; gap: 12px; width: 100%; text-align: left; background: transparent; border: 0; color: var(--fg); padding: 5px 8px; border-radius: 6px; cursor: pointer; font: inherit; }
  .play-row:hover { background: var(--hover); }
  .play-row .time { font-variant-numeric: tabular-nums; color: var(--fg-quiet); font-size: 12px; min-width: 44px; white-space: nowrap; flex-shrink: 0; }
  .play-row .name { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .play-row .artist { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; font-size: 12px; margin-left: auto; padding-left: 12px; max-width: 50%; }
  .radio img, .no-art.small { width: 36px; height: 36px; border-radius: 6px; object-fit: cover; }
  .replay { margin-left: auto; font-size: 12px; color: var(--accent); padding: 4px 10px; border-radius: 12px; border: 1px solid color-mix(in srgb, var(--accent) 50%, transparent); }
  .replay:hover { background: var(--accent); color: var(--on-accent); }
  .quiet { color: var(--fg-quiet); }
  .load-more { margin: 16px 0; }
</style>
