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
    for (const kind of kinds.filter(kind => kind === open || kind === 'lyrics').concat(open === 'artist' ? ['timeline'] : open === 'album' ? ['credits'] : [])) if (untrack(() => info[kind]) === undefined) {
      info = Object.assign({}, untrack(() => info), { [kind]: 'loading' })
      query('nowPlayingInfo', { kind, id: playing.id ?? 0, artistId: String(playing.artist_id ?? ''), albumId: String(playing.album_id ?? ''), artist: playing.artist ?? '', album: playing.album ?? '', title: playing.title ?? '', duration: Number(playing.duration ?? 0), remote: Number(playing.remote) === 1 ? 1 : 0 })
        .then(result => { if (infoFor === key) info = Object.assign({}, info, { [kind]: result ?? { found: false } }) })
    }
  })
  // Timeline albums you have: matched on the album title among this artist's own albums in the library, so they open from the timeline
  let ownedAlbums = $state<Record<string, { key: string, title: string }>>({}), ownedFor = ''
  const titleKey = (title: string) => String(title ?? '').toLowerCase().replace(/\s*[([][^)\]]*(remaster|edition|deluxe|expanded|anniversary|bonus)[^)\]]*[)\]]/g, '').replace(/&/g, 'and').replace(/[^\p{L}\p{N}]+/gu, '')
  $effect(() => {
    const artist = track?.artist ?? '', timeline = info.timeline
    if (!artist || !timeline?.found || ownedFor === artist) return
    ownedFor = artist
    personView('artist', artist).then(view => query('artistAlbums', { key: view.key, sort: 'year' })).then(albums => { const byTitle: Record<string, { key: string, title: string }> = {}; for (const album of albums?.own ?? []) byTitle[titleKey(album.title)] = { key: String(album.key), title: album.title }; if (ownedFor === artist) ownedAlbums = byTitle })
  })
  const timelineYears = (events: any[]) => { const years: { year: number, events: any[] }[] = []; for (const event of events ?? []) { const last = years[years.length - 1]; if (last && last.year === event.year) last.events.push(event); else years.push({ year: event.year, events: [event] }) } return years }
  const bornYear = $derived(info.timeline?.found && info.timeline.subheading === 'person' ? (info.timeline.events.find((event: any) => event.kind === 'start')?.year ?? 0) : 0)
  const timelineTitle = (event: any) => ({ member: `Joined ${event.title}`, label: `Signed to ${event.title}`, award: `Won ${event.title}`, nominated: `Nominated: ${event.title}` } as Record<string, string>)[event.kind] ?? event.title
  const timelineDetail = (event: any) => [event.detail, event.endYear && event.endYear !== event.year ? `until ${event.endYear}` : ''].filter(Boolean).join(' · ')
  const missing = (kind: string) => info[kind] && info[kind] !== 'loading' && !info[kind].found
  const chooseTab = (key: string) => { ui.infoTab = key; writeSetting('infoTab', key) }
  // The shell sends tidy blocks: paragraphs, "## " headings and "• " lists. Long text shows its opening, the rest behind Show more
  const blocksOf = (text: string) => text.split(/\n\s*\n/).map(block => block.trim()).filter(Boolean)
  const shortCount = (blocks: string[]) => { let length = 0, count = 0; for (const block of blocks) { if (count > 0 && length + block.length > 700) break; length += block.length; count++ } while (count > 1 && blocks[count - 1].startsWith('## ')) count--; return count } // never ends on a heading
  let expanded = $state(false)
  $effect(() => { trackKey; infoKey; expanded = false })
  const pending = (value: any) => value === undefined || value === 'loading'
  // Album is the review; Credits opens from a button above it, like the artist Timeline
  let showCredits = $state(false)
  const albumName = $derived(track?.album ?? '')
  $effect(() => { albumName; showCredits = false }) // a new album opens on its review
  // Artist is the bio; the Timeline opens from a button above it and closes back to the bio
  let showTimeline = $state(false)
  const artistName = $derived(track?.artist ?? '') // a string, so status polls (a new track object each time) don't count as a change
  $effect(() => { artistName; showTimeline = false }) // a new artist opens on their bio
  // Credits: who played, wrote and produced it. People are grouped by what they did, each with their roles and the tracks they're on; the playing track's people come first
  const creditGroups = [['performer', 'Performers'], ['writer', 'Writers'], ['production', 'Production'], ['artwork', 'Artwork and notes']]
  const playingNumber = (credits: any) => credits?.tracks?.find((item: any) => titleKey(item.title) === titleKey(track?.title ?? ''))?.n ?? 0
  const trackRange = (numbers: number[], tracks: any[]) => {
    const label = (number: number) => tracks.find(item => item.n === number)?.label ?? String(number), runs: string[] = []
    let first = numbers[0], last = numbers[0]
    for (const number of numbers.slice(1).concat([0])) { if (number === last + 1) { last = number; continue } runs.push(first === last ? label(first) : last === first + 1 ? `${label(first)}, ${label(last)}` : `${label(first)}–${label(last)}`); first = last = number }
    return runs.join(', ')
  }
  // One row per person: their roles, and a strip with a cell per track (filled where they play; the playing track's column lit)
  const peopleIn = (credits: any, group: string) => {
    const people: { name: string, roles: string[], tracks: Set<number>, detail: string[] }[] = [], everyTrack = credits.tracks.map((item: any) => item.n)
    for (const credit of credits.credits.filter((credit: any) => credit.group === group)) {
      let person = people.find(item => item.name === credit.name)
      if (!person) people.push(person = { name: credit.name, roles: [], tracks: new Set(), detail: [] })
      if (!person.roles.includes(credit.role)) person.roles.push(credit.role)
      for (const number of credit.all ? everyTrack : credit.tracks) person.tracks.add(number)
      person.detail.push(credit.all ? `${credit.role}: every track` : credit.tracks.length ? `${credit.role}: ${trackRange(credit.tracks, credits.tracks)}` : `${credit.role}: the album`)
    }
    return people
  }
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
          {:else if infoKey === 'lyrics'}
            {#if pending(result)}<p class="quiet">Looking…</p>{:else if !result.found}<p class="quiet">No lyrics found for this track</p>
            {:else}<div class="info-head"><div><h2>{result.heading}</h2>{#if result.subheading}<div class="quiet">{result.subheading}</div>{/if}</div></div><pre>{result.text}</pre>{@render source(result.source, result.link, 'Read more')}{/if}
          {:else if infoKey === 'artist'}
            {#if showTimeline && info.timeline?.found}
              <button class="back" onclick={() => showTimeline = false}>← Artist bio</button>
              {#if info.timeline?.found}
                <ol class="timeline">
                  {#each timelineYears(info.timeline.events) as group (group.year)}
                    <li class="year-group"><div class="year">{group.year}{#if bornYear && group.year > bornYear && !group.events.some(event => event.kind === 'start')}<span class="age">age {group.year - bornYear}</span>{/if}</div>
                      {#each group.events as event}
                        {@const owned = event.kind === 'album' ? ownedAlbums[titleKey(event.title)] : null}
                        <div class="event {event.kind}" class:playing={event.kind === 'album' && titleKey(event.title) === titleKey(track?.album ?? '')}>
                          <span class="marker" aria-hidden="true">{event.kind === 'award' ? '★' : event.kind === 'nominated' ? '☆' : ''}</span>
                          {#if event.kind === 'album'}<span class="sleeve"><img src={`https://coverartarchive.org/release-group/${event.id}/front-250`} alt="" loading="lazy" onerror={(failed) => (failed.currentTarget as HTMLImageElement).remove()} /></span>{/if}
                          <div class="event-text"><div class="event-title">{timelineTitle(event)}</div>{#if timelineDetail(event)}<div class="event-detail">{timelineDetail(event)}</div>{/if}</div>
                          {#if event.kind === 'album' && titleKey(event.title) === titleKey(track?.album ?? '')}<span class="now">Playing</span>{:else if owned}<button class="owned" title="Open the album" onclick={() => openView({ kind: 'album', key: owned.key, title: owned.title })}>In your library</button>{/if}
                        </div>
                      {/each}
                    </li>
                  {/each}
                </ol>
                {@render source(info.timeline.source, info.timeline.link, 'MusicBrainz page')}
              {/if}
            {:else}
              {#if info.timeline?.found}<button class="callout-button" onclick={() => showTimeline = true}><svg class="drawing" viewBox="0 0 64 24" aria-hidden="true"><path d="M2 19h60" /><path d="M10 19v-7M24 19v-11M38 19v-6M52 19v-10" /><rect x="6.5" y="5" width="7" height="7" rx="1.2" /><rect x="20.5" y="1" width="7" height="7" rx="1.2" /><rect x="34.5" y="6" width="7" height="7" rx="1.2" /><rect x="48.5" y="2" width="7" height="7" rx="1.2" /><circle cx="10" cy="19" r="2.2" /><circle cx="24" cy="19" r="2.2" /><circle cx="38" cy="19" r="2.2" /><circle cx="52" cy="19" r="2.2" /></svg><span>View artist timeline</span><span class="chevron" aria-hidden="true">›</span></button>{/if}
              {#if pending(result)}<p class="quiet">Looking…</p>
              {:else if !result.found}<p class="quiet">Nothing found about this artist</p>
              {:else}
                {@const blocks = blocksOf(result.text)}{@const shown = expanded ? blocks.length : shortCount(blocks)}
                <div class="info-head">{#if result.image}<img class="info-pic round" src={infoImage(result.image)} alt="" />{/if}<div><h2>{result.heading}</h2>{#if result.subheading}<div class="quiet">{result.subheading}</div>{/if}</div></div>
                {#each blocks.slice(0, shown) as block}{#if block.startsWith('## ')}<h3>{block.slice(3)}</h3>{:else if block.startsWith('• ')}<ul>{#each block.split('\n') as item}<li>{item.replace(/^• /, '')}</li>{/each}</ul>{:else}<p>{block}</p>{/if}{/each}
                {#if shown < blocks.length}<button class="more" onclick={() => expanded = true}>Show more</button>{/if}
                {@render source(result.source, result.link, 'Read more')}
              {/if}
            {/if}
          {:else}
            {#if showCredits && info.credits?.found}
              <button class="back" onclick={() => showCredits = false}>← Album</button>
                {@const credits = info.credits.events}{@const number = playingNumber(credits)}
                {#each creditGroups as [group, title]}{@const people = peopleIn(credits, group)}
                  {#if people.length}
                    <h3 class="credit-group">{title}</h3>
                    <div class="credit-list">
                      {#each people as person}
                        <div class="credit-row" class:here={person.tracks.has(number)} title={person.detail.join('\n')}>
                          <div class="credit-name">{person.name}</div><div class="credit-roles">{person.roles.join(', ')}</div>
                          <div class="track-map" style:width={`${credits.tracks.length * 6 - 2}px`} aria-hidden="true">{#if person.tracks.size}{#each credits.tracks as item}<i class:on={person.tracks.has(item.n)} class:current={item.n === number}></i>{/each}{/if}</div>
                        </div>
                      {/each}
                    </div>
                  {/if}
                {/each}
                {@render source(`${info.credits.source}${info.credits.subheading ? ` (the ${info.credits.subheading} release)` : ''}`, info.credits.link, 'MusicBrainz page')}
            {:else}
              {#if info.credits?.found}<button class="callout-button" onclick={() => showCredits = true}><svg class="drawing" viewBox="0 0 64 24" aria-hidden="true"><path d="M2 4h14M2 12h11M2 20h16" /><rect x="26" y="1.5" width="4" height="5" rx="0.8" class="on" /><rect x="33" y="1.5" width="4" height="5" rx="0.8" class="on" /><rect x="40" y="1.5" width="4" height="5" rx="0.8" class="on" /><rect x="47" y="1.5" width="4" height="5" rx="0.8" class="on" /><rect x="54" y="1.5" width="4" height="5" rx="0.8" class="on" /><rect x="26" y="9.5" width="4" height="5" rx="0.8" /><rect x="33" y="9.5" width="4" height="5" rx="0.8" class="on" /><rect x="40" y="9.5" width="4" height="5" rx="0.8" /><rect x="47" y="9.5" width="4" height="5" rx="0.8" class="on" /><rect x="54" y="9.5" width="4" height="5" rx="0.8" /><rect x="26" y="17.5" width="4" height="5" rx="0.8" class="on" /><rect x="33" y="17.5" width="4" height="5" rx="0.8" /><rect x="40" y="17.5" width="4" height="5" rx="0.8" /><rect x="47" y="17.5" width="4" height="5" rx="0.8" /><rect x="54" y="17.5" width="4" height="5" rx="0.8" class="on" /></svg><span>View album credits</span><span class="chevron" aria-hidden="true">›</span></button>{/if}
              {#if pending(result)}<p class="quiet">Looking…</p>
              {:else if !result.found}{#if !info.credits?.found}<p class="quiet">Nothing found about this album</p>{/if}
              {:else}
              {@const blocks = blocksOf(result.text)}{@const shown = expanded ? blocks.length : shortCount(blocks)}
              <div class="info-head">{#if result.image}<img class="info-pic" src={infoImage(result.image)} alt="" />{/if}<div><h2>{result.heading}</h2>{#if result.subheading}<div class="quiet">{result.subheading}</div>{/if}</div></div>
              {#each blocks.slice(0, shown) as block}{#if block.startsWith('## ')}<h3>{block.slice(3)}</h3>{:else if block.startsWith('• ')}<ul>{#each block.split('\n') as item}<li>{item.replace(/^• /, '')}</li>{/each}</ul>{:else}<p>{block}</p>{/if}{/each}
              {#if shown < blocks.length}<button class="more" onclick={() => expanded = true}>Show more</button>{/if}
              {@render source(result.source, result.link, 'Read more')}
              {/if}
            {/if}
          {/if}
        </div>
      {/if}
    </div>
  </div>
</section>

{#snippet source(from: string, link: string, label: string)}<div class="source">From {from}{#if link}&nbsp;· <button class="link" onclick={() => sendToShell({ type: 'openUrl', url: link })}>{label}</button>{/if}</div>{/snippet}

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
  .info { padding: 2px 4px 4px; user-select: text; text-align: left; } .info h2 { margin: 0 0 2px; font-size: 18px; } .info p { margin: 12px 0 0; line-height: 1.55; font-size: 13.5px; } .info .quiet { font-size: 12.5px; } .info h3 { margin: 18px 0 0; font-size: 14px; color: var(--fg); } .info ul { margin: 8px 0 0; padding-left: 18px; font-size: 13.5px; line-height: 1.5; } .info li { margin: 2px 0; }
  .info .more { margin-top: 10px; background: none; border: 0; padding: 0; color: var(--accent); font: inherit; font-size: 13px; font-weight: 600; cursor: pointer; } .info .more:hover { text-decoration: underline; }
  .info-head { display: flex; gap: 12px; align-items: center; } .info-pic { flex: none; width: 64px; height: 64px; object-fit: cover; border-radius: 8px; box-shadow: 0 6px 18px var(--shadow); } .info-pic.round { border-radius: 50%; }
  /* Timeline: a line down the left, years in the display face, albums with their sleeves; your own albums open from it */
  .timeline { list-style: none; margin: 4px 0 0; padding: 0 0 0 18px; position: relative; }
  .timeline::before { content: ""; position: absolute; left: 5px; top: 8px; bottom: 8px; width: 2px; border-radius: 1px; background: linear-gradient(var(--accent), color-mix(in srgb, var(--accent) 15%, transparent)); }
  .year-group { margin: 0 0 12px; } .year { position: relative; font-family: var(--font-display); font-size: 13px; color: var(--accent); letter-spacing: 0.04em; margin-bottom: 4px; }
  .year::before { content: ""; position: absolute; left: -17px; top: 4px; width: 10px; height: 10px; border-radius: 50%; background: var(--accent); box-shadow: 0 0 0 3px color-mix(in srgb, var(--accent) 25%, transparent); }
  .age { margin-left: 8px; color: var(--fg-quiet); font-family: var(--font-ui); font-size: 11.5px; letter-spacing: 0; }
  .event { display: flex; align-items: center; gap: 10px; padding: 4px 0; min-height: 22px; } .marker { flex: none; width: 12px; text-align: center; color: #e8b64c; font-size: 12px; }
  .sleeve { flex: none; width: 38px; height: 38px; border-radius: 4px; background: var(--panel-strong); overflow: hidden; box-shadow: 0 2px 8px var(--shadow); } .sleeve img { width: 100%; height: 100%; object-fit: cover; display: block; }
  .event-text { flex: 1; min-width: 0; } .event-title { font-size: 13.5px; line-height: 1.3; } .event-detail { font-size: 11.5px; color: var(--fg-quiet); }
  .event.album .event-title { font-weight: 600; } .event.start .event-title, .event.end .event-title { color: var(--fg-quiet); font-style: italic; } .event.nominated .event-title, .event.label .event-title, .event.member .event-title { color: color-mix(in srgb, var(--fg) 80%, var(--bg)); }
  .owned { flex: none; border: 1px solid color-mix(in srgb, var(--accent) 45%, transparent); background: var(--accent-soft); color: var(--accent); border-radius: 999px; padding: 2px 9px; font: inherit; font-size: 11px; font-weight: 600; cursor: pointer; } .now { flex: none; font-size: 11px; font-weight: 600; color: var(--on-accent); background: var(--accent); border-radius: 999px; padding: 2px 9px; } .event.playing .sleeve { box-shadow: 0 0 0 2px var(--accent), 0 2px 10px var(--shadow); } .owned:hover { background: color-mix(in srgb, var(--accent) 25%, transparent); }
  /* View artist timeline / View album credits: a small drawing of what opens as its badge, so it reads as something to open */
  .callout-button { display: flex; align-items: center; gap: 12px; width: 100%; margin: 0 0 16px; padding: 9px 12px; border-radius: 10px; border: 1px solid color-mix(in srgb, var(--accent) 35%, transparent); background: linear-gradient(120deg, color-mix(in srgb, var(--accent) 14%, transparent), color-mix(in srgb, var(--accent) 3%, transparent)); color: var(--fg); font: inherit; font-size: 13px; font-weight: 600; cursor: pointer; text-align: left; }
  .callout-button:hover { border-color: color-mix(in srgb, var(--accent) 60%, transparent); } .callout-button span:not(.chevron) { flex: 1; } .callout-button .chevron { color: var(--accent); font-size: 18px; line-height: 1; }
  .drawing { flex: none; width: 52px; height: 20px; fill: none; stroke: var(--accent); stroke-width: 1.6; stroke-linecap: round; } .drawing circle { fill: var(--accent); stroke: none; } .drawing rect { stroke-width: 1.3; fill: color-mix(in srgb, var(--accent) 30%, transparent); } .drawing rect.on { fill: var(--accent); }
  .back { background: none; border: 0; padding: 0; margin: 0 0 12px; color: var(--accent); font: inherit; font-size: 13px; font-weight: 600; cursor: pointer; } .back:hover { text-decoration: underline; }
  /* Credits: liner-notes table. Name, roles, and a strip with a cell per track; the playing track's column is lit down the whole list */
  .info h3.credit-group { margin: 22px 0 6px; font-size: 11.5px; text-transform: uppercase; letter-spacing: 0.08em; color: var(--fg-quiet); font-weight: 700; } .info .back + h3.credit-group { margin-top: 4px; }
  .credit-list { border-top: 1px solid var(--line); }
  .credit-row { display: grid; grid-template-columns: minmax(0, 0.95fr) minmax(0, 1.2fr) auto; gap: 12px; align-items: baseline; padding: 7px 4px 7px 8px; border-bottom: 1px solid var(--line); box-shadow: inset 2px 0 0 transparent; }
  .credit-row.here { box-shadow: inset 2px 0 0 var(--accent); background: color-mix(in srgb, var(--accent) 6%, transparent); }
  .credit-name { font-size: 13.5px; font-weight: 600; line-height: 1.35; } .credit-roles { font-size: 13px; color: var(--fg-quiet); line-height: 1.35; }
  .track-map { display: flex; gap: 2px; align-self: center; max-width: 120px; } .track-map i { flex: 1; min-width: 2px; max-width: 4px; width: 4px; height: 12px; border-radius: 1px; background: color-mix(in srgb, var(--fg) 12%, transparent); }
  .track-map i.on { background: color-mix(in srgb, var(--fg) 55%, transparent); } .track-map i.current { background: color-mix(in srgb, var(--accent) 30%, transparent); } .track-map i.on.current { background: var(--accent); }
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
