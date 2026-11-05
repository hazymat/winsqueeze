<script lang="ts">
  import { artUrl } from '../bridge'
  import { currentTrack, openView } from '../state.svelte'
  import Meters from './Meters.svelte'
  import Readout from './Readout.svelte'
  import QueueView from '../views/QueueView.svelte'
  // The one Now playing view: blurred-art backdrop, big art, track details, and the queue in its own scrolling glass panel
  const track = $derived(currentTrack())
  const cover = $derived(track?.coverid ? artUrl(track.coverid, 600) : '')
  const goTo = (kind: string, name: string) => openView({ kind, key: name.split(',')[0].trim().toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, ''), title: name.split(',')[0].trim() })
</script>

<section class="now-playing">
  {#if cover}<div class="backdrop" style:background-image={`url("${cover}")`}></div>{/if}
  <div class="layout">
    <div class="art-column">{#if cover}<img class="art" src={cover} alt="" />{:else}<div class="art empty">♪</div>{/if}</div>
    <div class="meta">
      <h1>{track?.title ?? 'Nothing playing'}</h1>
      {#if track?.artist}<button class="link artist" onclick={() => goTo('artist', track.artist)}>{track.artist}</button>{/if}
      {#if track?.album}<div class="album">{track.album}{track.year && track.year !== '0' ? ` · ${track.year}` : ''}</div>{/if}
      {#if track?.composer}<div class="quiet">Composer <button class="link" onclick={() => goTo('composer', track.composer)}>{track.composer}</button></div>{/if}
      {#if track?.conductor}<div class="quiet">Conductor {track.conductor}</div>{/if}
      {#if track?.band}<div class="quiet">Orchestra {track.band}</div>{/if}
      <div class="readout"><Readout /></div>
      <Meters size="large" />
    </div>
    <div class="queue-panel"><QueueView /></div>
  </div>
</section>

<style>
  .now-playing { position: relative; height: 100%; overflow: hidden; }
  .backdrop { position: absolute; inset: -80px; background-size: cover; background-position: center; filter: blur(70px) saturate(1.7) brightness(0.6); opacity: 0.75; pointer-events: none; }
  .layout { position: relative; height: 100%; box-sizing: border-box; display: grid; grid-template-columns: minmax(200px, 1fr) minmax(220px, 0.9fr) minmax(300px, 1.1fr); gap: 28px; padding: 28px 28px 20px; align-items: center; }
  .art-column { display: flex; justify-content: center; }
  .art { width: 100%; max-width: min(440px, 62vh); aspect-ratio: 1; border-radius: 12px; object-fit: cover; box-shadow: 0 24px 70px var(--shadow); }
  .art.empty { display: grid; place-items: center; font-size: 80px; background: var(--panel-strong); }
  .meta { display: flex; flex-direction: column; gap: 6px; min-width: 0; max-height: 100%; overflow-y: auto; padding-bottom: 4px; }
  /* Meters shrink with short windows so they're never cut off */
  .meta :global(.large .vu) { width: min(190px, 24vh); height: auto; }
  h1 { margin: 0; font-size: clamp(20px, 2.4vw, 32px); line-height: 1.15; }
  .link { background: none; border: 0; padding: 0; color: var(--fg); font: inherit; cursor: pointer; text-align: left; }
  .link.artist { font-size: 18px; }
  .quiet .link { color: inherit; text-decoration: underline; text-underline-offset: 2px; }
  .link:hover { color: var(--accent); }
  .album { color: var(--fg-quiet); font-size: 15px; }
  .quiet { color: var(--fg-quiet); }
  .readout { margin: 14px 0 6px; }
  /* The queue scrolls on its own, smoothly, with room for fingers */
  .queue-panel { align-self: stretch; min-height: 0; overflow-y: auto; overscroll-behavior: contain; scroll-behavior: smooth; -webkit-overflow-scrolling: touch; background: var(--glass); backdrop-filter: blur(var(--blur)) saturate(1.4); border: 1px solid var(--line); border-radius: 14px; padding: 14px; }
  @media (max-width: 1100px) { .layout { grid-template-columns: minmax(180px, 0.8fr) minmax(280px, 1.2fr); grid-template-rows: auto 1fr; } .art-column { grid-row: 1; } .meta { grid-row: 1; } .queue-panel { grid-column: 1 / 3; } .art { max-width: 260px; } }
  @media (max-width: 900px) { .now-playing { height: auto; min-height: 100%; } .layout { height: auto; grid-template-columns: 1fr; padding: 20px 14px; } .art-column, .meta, .queue-panel { grid-column: 1; grid-row: auto; } .art { max-width: 340px; } .meta { align-items: center; text-align: center; } .queue-panel { overflow: visible; } }
</style>
