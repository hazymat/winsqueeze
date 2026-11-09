<script lang="ts">
  import { untrack } from 'svelte'
  import { trackArt, serverArt, query, sendToShell } from '../bridge'
  import { isDamaged } from '../lib/damaged.svelte'
  import { app, currentTrack, openView, openSection, personView, ui, toast, writeSetting } from '../state.svelte'
  import Icon from './Icon.svelte'
  import Meters from './Meters.svelte'
  import Hint from './Hint.svelte'
  import Readout from './Readout.svelte'
  import QueueView from '../views/QueueView.svelte'
  // The one Now playing view: blurred-art backdrop, big art, track details, and the queue in its own scrolling glass panel
  const track = $derived(currentTrack())
  const cover = $derived(trackArt(track, 600))
  const goTo = (kind: string, name: string) => personView(kind, name).then(openView)
  // Artist, Album and Lyrics beside Up next (SPEC.md › Now playing views): about the playing track; Lyrics is looked up early so it can grey out when there are none
  const infoNames: Record<string, string> = { artist: 'Artist', album: 'Album', lyrics: 'Lyrics' }
  const infoTabs = $derived(app.trackInfoTabs)
  const infoKey = $derived(infoTabs.includes(ui.infoTab) ? ui.infoTab : 'queue')
  const trackKey = $derived(track ? [track.id, track.artist, track.album, track.title].join('|') : '')
  let info = $state<Record<string, any>>({}), infoFor = ''
  $effect(() => {
    const key = trackKey, open = infoKey, kinds = infoTabs, playing = track
    if (key !== infoFor) { infoFor = key; info = {} }
    if (!playing) return
    for (const kind of kinds) if ((kind === open || kind === 'lyrics') && untrack(() => info[kind]) === undefined) {
      info = Object.assign({}, untrack(() => info), { [kind]: 'loading' })
      query('trackInfo', { kind, id: playing.id ?? 0, artistId: String(playing.artist_id ?? ''), albumId: String(playing.album_id ?? ''), artist: playing.artist ?? '', album: playing.album ?? '', title: playing.title ?? '', duration: Number(playing.duration ?? 0), remote: Number(playing.remote) === 1 ? 1 : 0 })
        .then(result => { if (infoFor === key) info = Object.assign({}, info, { [kind]: result ?? { found: false } }) })
    }
  })
  const missing = (kind: string) => info[kind] && info[kind] !== 'loading' && !info[kind].found
  const chooseTab = (key: string) => { ui.infoTab = key; writeSetting('infoTab', key) }
  const paragraphs = (text: string) => text.split(/\n\s*\n|\n/).map(paragraph => paragraph.trim()).filter(Boolean)
  const infoImage = (image: string) => image.startsWith('http') ? image : serverArt(image, 300)
  // The album opens with this track marked (folder or tag album, whichever the library uses)
  async function openAlbum() { const trackId = Number(track?.id); const place = trackId > 0 ? await query('trackPlace', { id: trackId }) : null; if (place?.album) openView({ kind: 'album', key: String(place.album), title: place.albumTitle, highlight: String(trackId) }); else toast('That album isn’t in the library') }
</script>

<section class="now-playing">
  {#if cover && ui.coverBackdrop}<div class="backdrop" style:background-image={`url("${cover}")`}></div>{/if}<div class="grain" aria-hidden="true"></div>
  <div class="layout">
    <div class="art-column">{#if cover}<div class="art" class:stream={Number(app.nowPlaying?.remote) === 1}><img src={cover} alt="" /></div>{:else}<div class="art empty">♪</div>{/if}</div>
    <div class="meta">
      <h1>{track?.title ?? 'Nothing playing'}</h1>
      {#if track && isDamaged(track.id)}<div class="damaged">⚠ This file has damaged frames; playback continues past them. <button class="link" onclick={() => openSection('health')}>See damaged files</button></div>{/if}
      {#if track?.artist}<button class="link artist" onclick={() => goTo('artist', track.artist)}>{track.artist}</button>{/if}
      {#if track?.album}<div class="album"><button class="link album-link" onclick={openAlbum} title="Open the album">{track.album}</button>{track.year && track.year !== '0' ? ` · ${track.year}` : ''}</div>{/if}
      {#if track?.composer}<div class="quiet">Composer <button class="link" onclick={() => goTo('composer', track.composer)}>{track.composer}</button></div>{/if}
      {#if track?.conductor}<div class="quiet">Conductor {track.conductor}</div>{/if}
      {#if track?.band}<div class="quiet">Orchestra {track.band}</div>{/if}
      <div class="readout"><Readout /></div>
      <Meters size="large" /><Hint id="meters-clicks" text="Left-click to cycle meter visualisations. Right-click to maximise the visualisation." />
    </div>
    <div class="queue-panel" class:tabbed={infoTabs.length > 0}>
      {#if infoTabs.length}
        <div class="info-tabs" role="tablist">
          <button role="tab" class="queue-tab" class:on={infoKey === 'queue'} aria-selected={infoKey === 'queue'} onclick={() => chooseTab('queue')}><Icon name="queue" size={15} />Up next</button>
          <div class="track-group" title="About the track that's playing">{#if cover}<span class="this-track"><img src={cover} alt="" /></span>{/if}{#each infoTabs as kind (kind)}<button role="tab" class:on={infoKey === kind} class:none={kind === 'lyrics' && missing('lyrics')} aria-selected={infoKey === kind} title={kind === 'lyrics' && missing('lyrics') ? 'No lyrics found for this track' : ''} onclick={() => chooseTab(kind)}>{infoNames[kind]}</button>{/each}</div>
        </div>
      {/if}
      {#if infoKey === 'queue'}<div class="queue-body"><QueueView /></div>
      {:else}
        {@const result = info[infoKey]}
        <div class="info" class:lyrics={infoKey === 'lyrics'}>
          {#if !track}<p class="quiet">Nothing playing</p>
          {:else if !result || result === 'loading'}<p class="quiet">Looking…</p>
          {:else if !result.found}<p class="quiet">{infoKey === 'lyrics' ? 'No lyrics found for this track' : infoKey === 'album' ? 'Nothing found about this album' : 'Nothing found about this artist'}</p>
          {:else}
            <div class="info-head">{#if result.image}<img class="info-pic" class:round={infoKey === 'artist'} src={infoImage(result.image)} alt="" />{/if}<div><h2>{result.heading}</h2>{#if result.subheading}<div class="quiet">{result.subheading}</div>{/if}</div></div>
            {#if infoKey === 'lyrics'}<pre>{result.text}</pre>{:else}{#each paragraphs(result.text) as paragraph}<p>{paragraph}</p>{/each}{/if}
            <div class="source">From {result.source}{#if result.link}&nbsp;· <button class="link" onclick={() => sendToShell({ type: 'openUrl', url: result.link })}>Read more</button>{/if}</div>
          {/if}
        </div>
      {/if}
    </div>
  </div>
</section>

<style>
  .now-playing { position: relative; height: 100%; overflow: hidden; container: now-playing / size; } /* art sizes to the space actually left (cqh/cqw) */
  .grain { position: absolute; inset: 0; pointer-events: none; opacity: 0.035; background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='160' height='160'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='2' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23n)'/%3E%3C/svg%3E"); } /* a touch of noise breaks up banding in the blurred backdrop */
  .backdrop { position: absolute; inset: -80px; background-size: cover; background-position: center; filter: blur(70px) saturate(1.7) brightness(0.6); opacity: 0.75; pointer-events: none; }
  .layout { position: relative; height: 100%; box-sizing: border-box; display: grid; grid-template-columns: minmax(200px, 1fr) minmax(220px, 0.9fr) minmax(300px, 1.1fr); gap: 28px; padding: 28px 28px 20px; align-items: center; }
  .art-column { display: flex; justify-content: center; }
  .damaged { color: var(--warn); font-size: 13px; margin: 2px 0 6px; }
  .damaged .link { color: inherit; text-decoration: underline; }
  .art { width: 100%; max-width: min(440px, 80cqh); aspect-ratio: 1; border-radius: 12px; overflow: hidden; box-shadow: 0 24px 70px var(--shadow); }
  .art img { display: block; width: 100%; height: 100%; object-fit: cover; }
  .art.stream img { transform: scale(1.04); } /* station logos and the stream placeholder carry their own square frame: crop it so only our rounded corner shows */
  .art.empty { display: grid; place-items: center; font-size: 80px; background: var(--panel-strong); }
  .meta { display: flex; flex-direction: column; gap: 6px; min-width: 0; max-height: 100%; overflow-y: auto; padding-bottom: 4px; }
  /* Meters shrink with short windows so they're never cut off */
  .meta { container-type: inline-size; overflow-x: hidden; } /* cqw below = the details column, so meters never overflow it (no sideways scrollbar at any scaling) */
  .meta :global(.large .vu) { width: min(190px, 24vh, (100cqw - 4px) / 2); height: auto; } .meta :global(.large .panel) { width: min(384px, 48vh + 4px, 30vh * var(--aspect), 100cqw); } .meta :global(.large .spectrum) { width: min(320px, 100cqw); }
  .readout :global(.readout) { flex-wrap: wrap; row-gap: 2px; }
  h1 { margin: 0; font-size: clamp(20px, 2.4vw, 32px); line-height: 1.15; }
  .link { background: none; border: 0; padding: 0; color: var(--fg); font: inherit; cursor: pointer; text-align: left; }
  .link.artist { font-size: 18px; }
  .quiet .link { color: inherit; text-decoration: underline; text-underline-offset: 2px; }
  .link:hover { color: var(--accent); text-decoration: underline; text-underline-offset: 3px; } .album-link { color: inherit; }
  .album { color: var(--fg-quiet); font-size: 15px; }
  .quiet { color: var(--fg-quiet); }
  .readout { margin: 14px 0 6px; }
  /* The queue scrolls on its own, smoothly, with room for fingers */
  .queue-panel { align-self: stretch; min-height: 0; overflow-y: auto; overscroll-behavior: contain; scroll-behavior: smooth; -webkit-overflow-scrolling: touch; /* a skin-coloured floor under the glass, so text stays readable over light covers and see-through glass */ --fg-quiet: color-mix(in srgb, var(--fg) 70%, var(--bg)); background: linear-gradient(color-mix(in srgb, var(--bg) 45%, transparent), color-mix(in srgb, var(--bg) 45%, transparent)), var(--glass); backdrop-filter: blur(var(--blur)) saturate(1.4); border: 1px solid var(--line); border-radius: 14px; padding: 14px; }
  /* Up next on its own; Artist, Album and Lyrics grouped behind the playing cover, since they're about this track */
  .queue-panel.tabbed { display: flex; flex-direction: column; overflow: hidden; } .queue-body, .info { flex: 1; min-height: 0; overflow-y: auto; overscroll-behavior: contain; }
  .tabbed .queue-body :global(.head h3) { display: none; } /* the Up next tab names it */
  .info-tabs { display: flex; gap: 10px; margin: 0 0 12px; flex: none; align-items: stretch; }
  .info-tabs button { background: none; border: 0; border-radius: 7px; padding: 6px 10px; color: var(--fg-quiet); font: inherit; font-size: 13px; font-weight: 600; cursor: pointer; white-space: nowrap; }
  .info-tabs button:hover { color: var(--fg); } .info-tabs button.on { background: var(--accent-soft); color: var(--accent); } .info-tabs button.none { opacity: 0.4; }
  .info-tabs .queue-tab { display: flex; align-items: center; gap: 6px; border: 1px solid var(--line); background: var(--panel); border-radius: 9px; padding: 6px 12px; } .info-tabs .queue-tab.on { border-color: color-mix(in srgb, var(--accent) 40%, transparent); }
  .track-group { flex: 1; display: flex; align-items: center; gap: 2px; padding: 3px; border-radius: 9px; background: var(--panel); border: 1px solid var(--line); min-width: 0; } .track-group button { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; }
  .this-track { flex: none; width: 26px; height: 26px; margin: 0 4px 0 1px; border-radius: 5px; overflow: hidden; box-shadow: 0 1px 4px var(--shadow); } .this-track img { width: 100%; height: 100%; display: block; object-fit: cover; }
  .info { padding: 2px 4px 4px; user-select: text; text-align: left; } .info h2 { margin: 0 0 2px; font-size: 18px; } .info p { margin: 12px 0 0; line-height: 1.55; font-size: 13.5px; } .info .quiet { font-size: 12.5px; }
  .info-head { display: flex; gap: 12px; align-items: center; } .info-pic { flex: none; width: 64px; height: 64px; object-fit: cover; border-radius: 8px; box-shadow: 0 6px 18px var(--shadow); } .info-pic.round { border-radius: 50%; }
  .lyrics pre { margin: 14px 0 0; font: inherit; font-size: 15px; line-height: 1.7; white-space: pre-wrap; }
  .source { margin-top: 18px; padding-top: 10px; border-top: 1px solid var(--line); color: var(--fg-quiet); font-size: 11.5px; } .source .link { color: inherit; text-decoration: underline; text-underline-offset: 2px; }
  @media (max-width: 1100px) { .layout { grid-template-columns: minmax(180px, 0.8fr) minmax(280px, 1.2fr); grid-template-rows: auto 1fr; } .art-column { grid-row: 1; } .meta { grid-row: 1; } .queue-panel { grid-column: 1 / 3; } .art { max-width: min(260px, 42cqh); } }
  @media (max-width: 900px) { .queue-panel.tabbed, .queue-body, .info { overflow: visible; } .now-playing { overflow-y: auto; } .layout { height: auto; min-height: 100%; grid-template-columns: 1fr; padding: 20px 14px; } .art-column, .meta, .queue-panel { grid-column: 1; grid-row: auto; } .art { max-width: min(340px, 62cqh, 100cqw - 28px); } .meta { align-items: center; text-align: center; overflow: visible; max-height: none; } .queue-panel { overflow: visible; } }
  /* Short and narrow: art beside the details, so it's never cut off */
  @container now-playing (max-width: 900px) and (max-height: 560px) { .layout { grid-template-columns: auto minmax(0, 1fr); gap: 14px 18px; } .art-column { grid-column: 1; } .meta { grid-column: 2; align-items: flex-start; text-align: left; } .queue-panel { grid-column: 1 / 3; } .art { width: min(200px, 45cqh, 38cqw); } .meta :global(.large .vu) { width: min(120px, 22cqw); } }
  /* Short windows: the meters move to the right of the details and grow to the height available (VU face is 5:3); the queue shows only when there's width for it */
  @container now-playing (max-height: 460px) { .now-playing { overflow: hidden; } .layout { height: 100%; min-height: 0; grid-template-columns: auto minmax(0, 1fr) minmax(280px, 0.9fr); grid-template-rows: minmax(0, 1fr); gap: 24px; padding: 16px 20px; } .art-column, .meta, .queue-panel { grid-column: auto; grid-row: 1; } .art { width: min(240px, 100cqh - 32px, 24cqw); max-width: none; } .meta { container-type: inline-size; display: grid; grid-template-columns: minmax(180px, 1fr) auto; column-gap: 24px; align-content: center; align-items: start; text-align: left; overflow: hidden; } .meta > :global(*) { grid-column: 1; } .meta > :global(.meters) { grid-column: 2; grid-row: 1 / span 12; align-self: center; } .meta :global(.large .vu) { width: min(190px, (100cqh - 40px) * 1.66, (100cqw - 212px) / 2); } .meta :global(.large .panel) { width: min(384px, (100cqh - 40px) * var(--aspect), 100cqw - 208px); } /* cqh: the view's height; cqw: the details column's width, leaving 180px for the text */ .readout { margin: 8px 0 0; min-width: 0; } .readout :global(.readout) { flex-wrap: wrap; white-space: normal; } }
  @container now-playing (max-height: 460px) and (max-width: 1100px) { .layout { grid-template-columns: auto minmax(0, 1fr); } .queue-panel { display: none; } }
</style>
