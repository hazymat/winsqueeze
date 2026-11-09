<script lang="ts">
  import { t } from '../lib/i18n'
  import { command, sendToShell } from '../bridge'
  import { app } from '../state.svelte'
  import { downloads } from '../lib/downloads.svelte'
  import Icon from './Icon.svelte'
  // Glass popover instead of the native select (whose Windows popup ignores the skin)
  // Lives at the top of the sidebar, so the menu opens downwards; this PC's output device sits under it (click: Settings › Audio)
  let { placement = 'down' }: { placement?: 'up' | 'down' } = $props()
  let open = $state(false)
  const selectedId = $derived(app.selectedPlayer || app.ownPlayer)
  const selected = $derived(app.players.find(player => player.playerid === selectedId))
  const choose = (playerId: string) => { app.selectedPlayer = playerId; sendToShell({ type: 'selectPlayer', id: playerId }); open = false }
  // Power and sleep are Lyrion's own player commands, so other controllers see them too; the offline server has neither
  const online = $derived(!app.status?.offline)
  const playerCommand = (playerId: string, parts: unknown[], quiet = false) => sendToShell({ type: 'playerCommand', id: playerId, command: parts, quiet })
  const setPower = (player: any) => { player.power = Number(player.power) ? 0 : 1; playerCommand(player.playerid, ['power', player.power]) }
  // Multi-room: Lyrion's sync groups. The link button joins a player to the selected one's group (Lyrion's "<selected> sync <other>") or takes it out ("<other> sync -")
  const groupOf = (playerId: string) => app.syncGroups.find(members => members.includes(playerId)) ?? []
  const selectedGroup = $derived(groupOf(selectedId))
  const grouped = (playerId: string) => playerId !== selectedId && selectedGroup.includes(playerId)
  const nameOf = (playerId: string) => app.players.find(player => player.playerid === playerId)?.name ?? ''
  const otherGroupNote = (playerId: string) => { const members = groupOf(playerId); return members.length && !members.includes(selectedId) ? ' · ' + t('with {players}', { players: members.filter(memberId => memberId !== playerId).map(nameOf).join(', ') }) : '' }
  function toggleGroup(player: any) {
    const joining = !grouped(player.playerid)
    app.syncGroups = joining ? app.syncGroups.map(members => members.filter(memberId => memberId !== player.playerid)).filter(members => members.length > 1).concat(selectedGroup.length ? [] : [[selectedId]]).map(members => members.includes(selectedId) ? members.concat([player.playerid]) : members) : app.syncGroups.map(members => members.filter(memberId => memberId !== player.playerid)).filter(members => members.length > 1)
    if (joining) playerCommand(selectedId, ['sync', player.playerid]); else playerCommand(player.playerid, ['sync', '-'])
  }
  // Grouped players sit straight under the selected one, so the group reads as one block
  const orderedPlayers = $derived(app.players.filter(player => !grouped(player.playerid)).flatMap(player => player.playerid === selectedId ? [player].concat(app.players.filter(member => grouped(member.playerid))) : [player]))
  let volumeSent = 0
  function setMemberVolume(playerId: string, event: Event, final: boolean) {
    const volume = Number((event.target as HTMLInputElement).value); app.groupVolumes[playerId] = volume
    if (final || Date.now() - volumeSent > 80) { volumeSent = Date.now(); playerCommand(playerId, ['mixer', 'volume', volume], true) }
  }
  const sleepLeft = $derived(Math.round(Number(app.nowPlaying?.will_sleep_in ?? 0)))
  const trackLeft = $derived(Math.max(0, Math.ceil(Number(app.nowPlaying?.duration ?? 0) - Number(app.nowPlaying?.time ?? 0))))
  const sleepChoices = [15, 30, 45, 60, 90]
  const sleepFor = (seconds: number) => command('sleep', seconds)
</script>

<svelte:window onmousedown={(event) => { if (open && !(event.target as HTMLElement).closest('.picker')) open = false }} onkeydown={(event) => { if (open && event.key === 'Escape') { event.preventDefault(); open = false } }} />

<div class="picker">
  <button class="current" onclick={() => open = !open} aria-haspopup="listbox" aria-expanded={open} title={t('Choose player')}>
    <Icon name="speaker" size={12} /><span class="name">{selected?.name ?? t('Player')}{selectedGroup.length > 1 ? ` + ${selectedGroup.length - 1}` : ''}</span>{#if sleepLeft > 0}<span class="moon" title={t('Sleep timer: {count} min left', { count: Math.ceil(sleepLeft / 60) })}>☾</span>{/if}<span class="chevron" class:down={placement === 'down'}>⌃</span>
  </button>
  {#if open}
    <div class="menu {placement}" role="listbox">
      {#each orderedPlayers as player (player.playerid)}
        <div class="row" class:grouped={grouped(player.playerid)}>
          <button role="option" aria-selected={player.playerid === selectedId} class:on={player.playerid === selectedId} disabled={!player.connected} onclick={() => choose(player.playerid)}>
            <span class="dot" class:playing={player.isplaying}></span>
            <span class="text"><span class="player-name">{player.name}</span><span class="detail">{player.playerid === app.ownPlayer ? t('This PC') : player.modelname ?? player.model ?? ''}{player.connected ? '' : ' · ' + t('offline')}{player.connected && online && !Number(player.power) ? ' · ' + t('off') : ''}{online ? otherGroupNote(player.playerid) : ''}</span></span>
            {#if player.playerid === selectedId}<span class="tick">✓</span>{/if}
          </button>
          {#if (player.connected || grouped(player.playerid)) && online && player.playerid !== selectedId && selected?.connected}<button class="link" class:on={grouped(player.playerid)} onclick={() => toggleGroup(player)} title={grouped(player.playerid) ? t('Stop playing {player} with {other}', { player: player.name, other: selected?.name }) : t('Play {player} together with {other}', { player: player.name, other: selected?.name })} aria-label={t('Group')} aria-pressed={grouped(player.playerid)}><svg viewBox="0 0 16 16" width="13" height="13" aria-hidden="true"><path d="M6.5 9.5l3-3M7 4.5l1.2-1.2a2.6 2.6 0 0 1 3.7 3.7L10.7 8.2M9 11.5l-1.2 1.2a2.6 2.6 0 0 1-3.7-3.7l1.2-1.2" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" /></svg></button>{/if}
          {#if player.connected && online}<button class="power" class:on={Number(player.power) === 1} onclick={() => setPower(player)} title={Number(player.power) ? t('Turn off') : t('Turn on')} aria-label={t('Power')} aria-pressed={Number(player.power) === 1}><svg viewBox="0 0 16 16" width="13" height="13" aria-hidden="true"><path d="M8 2v6M4.6 4.2a5 5 0 1 0 6.8 0" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" /></svg></button>{/if}
        </div>
        {#if grouped(player.playerid) && player.connected}<input class="member-volume" type="range" min="0" max="100" value={app.groupVolumes[player.playerid] ?? 50} oninput={(event) => setMemberVolume(player.playerid, event, false)} onchange={(event) => setMemberVolume(player.playerid, event, true)} title={t('{player} volume', { player: player.name })} aria-label={t('{player} volume', { player: player.name })} />{/if}
        {#if player.playerid === app.ownPlayer && app.status?.device}
          <button class="output" onclick={() => { open = false; sendToShell({ type: 'openSettings', tab: 'Audio' }) }} title={t('Audio settings')}><Icon name="volume" size={12} /><span class="output-name">{app.status.device}</span><span class="output-hint">{t('Change')}</span></button>
        {/if}
      {/each}
      {#if !app.players.length}<div class="empty">{t('No players found')}</div>{/if}
      {#if online && selected?.connected}
        <div class="menu-title sleep-title"><span>{t('Sleep')}{sleepLeft > 0 ? ' · ' + t('{count} min left', { count: Math.ceil(sleepLeft / 60) }) : ''}</span>{#if sleepLeft > 0}<button class="sleep-off" onclick={() => sleepFor(0)}>{t('Turn off')}</button>{/if}</div>
        <!-- Two even rows of three: the times in order, then End of track -->
        <div class="sleep">
          {#each sleepChoices as minutes (minutes)}<button class="chip" onclick={() => sleepFor(minutes * 60)} title={t('Stop in {count} minutes', { count: minutes })}>{t('{count} min', { count: minutes })}</button>{/each}
          <button class="chip" disabled={!trackLeft} onclick={() => sleepFor(trackLeft)} title={t('Stop when this track ends')}>{t('End of track')}</button>
        </div>
      {/if}
      <!-- Offline: this PC plays its downloads without the server (SPEC.md › Offline downloads) -->
      {#if app.status?.offline}<button class="output" onclick={() => { open = false; sendToShell({ type: 'goOnline' }) }} title={t('Reconnect to the server')}><Icon name="download" size={12} /><span class="output-name">{t('Playing offline')}</span><span class="output-hint">{t('Go online')}</span></button>
      {:else if downloads.finishedTracks}<button class="output" onclick={() => { open = false; sendToShell({ type: 'goOffline' }) }} title={t('Play downloads on this PC without the server')}><Icon name="download" size={12} /><span class="output-name">{t('Downloads only')}</span><span class="output-hint">{t('Play offline')}</span></button>{/if}
    </div>
  {/if}
</div>

<style>
  .picker { position: relative; }
  .current { display: flex; align-items: center; gap: 6px; width: 100%; height: 24px; background: var(--panel); color: var(--fg); border: 1px solid var(--line); border-radius: 7px; padding: 0 8px; font: inherit; font-size: 11.5px; cursor: pointer; }
  .current:hover { background: var(--hover); }
  .name { flex: 1; text-align: left; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .chevron { font-size: 9px; color: var(--fg-quiet); } .chevron.down { transform: rotate(180deg); }
  .menu { position: absolute; left: 0; min-width: 260px; max-height: 50vh; overflow-y: auto; z-index: 40; padding: 6px; background: linear-gradient(color-mix(in srgb, var(--panel-solid) 50%, transparent), color-mix(in srgb, var(--panel-solid) 50%, transparent)), var(--panel-solid); backdrop-filter: blur(32px) saturate(1.4); border: 1px solid var(--line); border-radius: 12px; /* half a second layer of the panel colour: a touch less see-through than the plain panel, still glass */ box-shadow: 0 16px 40px var(--shadow); }
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
  .menu.up { bottom: calc(100% + 8px); } .menu.down { top: calc(100% + 6px); }
  .menu button.output { margin: -2px 0 4px 17px; width: calc(100% - 17px); padding: 4px 10px; gap: 6px; font-size: 11.5px; color: var(--fg-quiet); }
  .output-name { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; } .output-hint { font-size: 10.5px; color: var(--accent); opacity: 0; } .menu button.output:hover .output-hint { opacity: 1; }
  .row { display: flex; align-items: center; gap: 2px; } .row > button[role="option"] { flex: 1; min-width: 0; }
  .menu button.power { width: 28px; height: 28px; flex-shrink: 0; justify-content: center; padding: 0; color: var(--fg-quiet); opacity: 0.6; } .menu button.power.on { color: #3ddc84; opacity: 1; }
  .menu button.link { width: 28px; height: 28px; flex-shrink: 0; justify-content: center; padding: 0; color: var(--fg-quiet); opacity: 0.6; } .menu button.link.on { color: var(--accent); opacity: 1; }
  .row.grouped { margin-left: 17px; }
  .member-volume { display: block; width: calc(100% - 44px); margin: -2px 0 6px 44px; accent-color: var(--accent); height: 14px; }
  .moon { color: var(--accent); font-size: 12px; line-height: 1; }
  .menu-title { padding: 10px 10px 5px; color: var(--fg-quiet); font-size: 10.5px; text-transform: uppercase; letter-spacing: 0.06em; font-weight: 600; border-top: 1px solid var(--line); margin-top: 4px; }
  .sleep { display: grid; grid-template-columns: repeat(3, 1fr); gap: 5px; padding: 0 8px 8px; }
  .menu button.chip { justify-content: center; padding: 5px 0; font-size: 11.5px; border: 1px solid var(--line); border-radius: 7px; white-space: nowrap; }
  .sleep-title { display: flex; align-items: center; justify-content: space-between; }
  .menu button.sleep-off { width: auto; padding: 0; font-size: 11px; text-transform: none; letter-spacing: 0; color: var(--accent); background: none; } .menu button.sleep-off:hover { background: none; text-decoration: underline; }
  .empty { padding: 8px 10px; color: var(--fg-quiet); font-size: 12px; }
</style>
