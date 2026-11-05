<script lang="ts">
  import { artUrl } from '../bridge'
  import { app, ui, currentTrack, openView } from '../state.svelte'
  import Meters from './Meters.svelte'
  import Readout from './Readout.svelte'
  import QueueView from '../views/QueueView.svelte'
  // Expanded now playing: blurred-art backdrop, large art, metadata, readout, large meters, the queue alongside
  const track = $derived(currentTrack())
  const cover = $derived(track?.coverid ? artUrl(track.coverid, 600) : '')
  const goTo = (kind: string, name: string) => { ui.expanded = false; openView({ kind, key: name.split(',')[0].trim().toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, ''), title: name.split(',')[0].trim() }) }
</script>

<section class="now-playing">
  {#if cover}<div class="backdrop" style:background-image={`url("${cover}")`}></div>{/if}
  <button class="close" onclick={() => ui.expanded = false} aria-label="Close now playing">⌄</button>
  <div class="layout">
    <div class="main">
      {#if cover}<img class="art" src={cover} alt="" />{:else}<div class="art empty">♪</div>{/if}
      <div class="meta">
        <h1>{track?.title ?? 'Nothing playing'}</h1>
        {#if track?.artist}<button class="link" onclick={() => goTo('artist', track.artist)}>{track.artist}</button>{/if}
        {#if track?.album}<div class="album">{track.album}{track.year && track.year !== '0' ? ` · ${track.year}` : ''}</div>{/if}
        {#if track?.composer}<div class="quiet">Composer: <button class="link" onclick={() => goTo('composer', track.composer)}>{track.composer}</button></div>{/if}
        {#if track?.conductor}<div class="quiet">Conductor: {track.conductor}</div>{/if}
        {#if track?.band}<div class="quiet">Orchestra: {track.band}</div>{/if}
        <div class="readout"><Readout /></div>
        <Meters size="large" active={false} />
      </div>
    </div>
    <div class="queue"><h3>Up next</h3><QueueView compact={true} /></div>
  </div>
</section>

<style>
  .now-playing { position: absolute; inset: 0; z-index: 10; overflow: hidden; background: var(--bg); display: flex; flex-direction: column; }
  .backdrop { position: absolute; inset: -60px; background-size: cover; background-position: center; filter: blur(60px) saturate(1.6) brightness(0.55); opacity: 0.8; }
  .close { position: absolute; top: 12px; right: 16px; z-index: 2; background: var(--glass); border: 1px solid var(--line); color: var(--fg); border-radius: 50%; width: 36px; height: 36px; cursor: pointer; font-size: 18px; }
  .layout { position: relative; flex: 1; display: grid; grid-template-columns: minmax(0, 1.5fr) minmax(260px, 1fr); gap: 32px; padding: 40px; min-height: 0; }
  .main { display: flex; gap: 32px; align-items: center; min-width: 0; flex-wrap: wrap; align-content: center; }
  .art { width: min(42vh, 420px); aspect-ratio: 1; border-radius: 10px; object-fit: cover; box-shadow: 0 20px 60px var(--shadow); }
  .art.empty { display: grid; place-items: center; font-size: 80px; background: var(--panel-strong); }
  .meta { display: flex; flex-direction: column; gap: 6px; min-width: 240px; flex: 1; }
  h1 { margin: 0; font-size: 30px; line-height: 1.15; }
  .link { background: none; border: 0; padding: 0; color: var(--fg); font: inherit; font-size: 18px; cursor: pointer; text-align: left; }
  .quiet .link { font-size: inherit; color: inherit; text-decoration: underline; }
  .link:hover { color: var(--accent); }
  .album { color: var(--fg-quiet); font-size: 16px; }
  .quiet { color: var(--fg-quiet); }
  .readout { margin: 12px 0 6px; }
  .queue { background: var(--glass); backdrop-filter: blur(var(--blur)); border: 1px solid var(--line); border-radius: 12px; padding: 12px; overflow-y: auto; min-height: 0; }
  h3 { margin: 0 0 8px; }
  @media (max-width: 900px) { .layout { grid-template-columns: 1fr; padding: 24px 16px; overflow-y: auto; } .main { justify-content: center; } .art { width: min(70vw, 360px); } .queue { overflow: visible; } }
</style>
