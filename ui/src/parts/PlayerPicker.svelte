<script lang="ts">
  import { sendToShell } from '../bridge'
  import { app } from '../state.svelte'
  import Icon from './Icon.svelte'
  // Glass popover instead of the native select (whose Windows popup ignores the skin)
  let open = $state(false)
  const selectedId = $derived(app.selectedPlayer || app.ownPlayer)
  const selected = $derived(app.players.find(player => player.playerid === selectedId))
  const choose = (playerId: string) => { app.selectedPlayer = playerId; sendToShell({ type: 'selectPlayer', id: playerId }); open = false }
</script>

<svelte:window onmousedown={(event) => { if (open && !(event.target as HTMLElement).closest('.picker')) open = false }} onkeydown={(event) => { if (event.key === 'Escape') open = false }} />

<div class="picker">
  <button class="current" onclick={() => open = !open} aria-haspopup="listbox" aria-expanded={open} title="Choose player">
    <Icon name="speaker" size={14} /><span class="name">{selected?.name ?? 'Player'}</span><span class="chevron">⌃</span>
  </button>
  {#if open}
    <div class="menu" role="listbox">
      {#each app.players as player (player.playerid)}
        <button role="option" aria-selected={player.playerid === selectedId} class:on={player.playerid === selectedId} disabled={!player.connected} onclick={() => choose(player.playerid)}>
          <span class="dot" class:playing={player.isplaying}></span>
          <span class="text"><span class="player-name">{player.name}</span><span class="detail">{player.playerid === app.ownPlayer ? 'This PC' : player.modelname ?? player.model ?? ''}{player.connected ? '' : ' · offline'}</span></span>
          {#if player.playerid === selectedId}<span class="tick">✓</span>{/if}
        </button>
      {/each}
      {#if !app.players.length}<div class="empty">No players found</div>{/if}
    </div>
  {/if}
</div>

<style>
  .picker { position: relative; }
  .current { display: flex; align-items: center; gap: 6px; max-width: 160px; background: var(--panel); color: var(--fg); border: 1px solid var(--line); border-radius: 8px; padding: 4px 8px; font: inherit; font-size: 12px; cursor: pointer; }
  .current:hover { background: var(--hover); }
  .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .chevron { font-size: 9px; color: var(--fg-quiet); }
  .menu { position: absolute; bottom: calc(100% + 8px); right: 0; min-width: 230px; max-height: 50vh; overflow-y: auto; z-index: 40; padding: 6px; background: var(--panel-solid); backdrop-filter: blur(24px) saturate(1.5); border: 1px solid var(--line); border-radius: 12px; box-shadow: 0 16px 40px var(--shadow); }
  .menu button { display: flex; align-items: center; gap: 10px; width: 100%; background: none; border: 0; color: var(--fg); font: inherit; text-align: left; padding: 7px 10px; border-radius: 8px; cursor: pointer; }
  .menu button:hover:not(:disabled) { background: var(--hover); }
  .menu button.on { background: var(--accent-soft); }
  .menu button:disabled { opacity: 0.45; cursor: default; }
  .dot { width: 7px; height: 7px; border-radius: 50%; background: var(--fg-quiet); flex-shrink: 0; opacity: 0.5; }
  .dot.playing { background: #3ddc84; opacity: 1; box-shadow: 0 0 6px #3ddc84; }
  .text { flex: 1; min-width: 0; display: flex; flex-direction: column; }
  .player-name { font-size: 13px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .detail { font-size: 11px; color: var(--fg-quiet); }
  .tick { color: var(--accent); font-weight: 700; }
  .empty { padding: 8px 10px; color: var(--fg-quiet); font-size: 12px; }
</style>
