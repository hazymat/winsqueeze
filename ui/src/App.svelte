<script lang="ts">
  import { sendToShell, onShellMessage } from './bridge'
  type Status = { version: string; updateReady: string; server: string; state: string; message: string; device: string; driver: string; exclusive: boolean; sampleRate: number; fallback: boolean }
  let status = $state<Status | null>(null)
  onShellMessage((message) => { if (message.type === 'status') status = message as unknown as Status })
  sendToShell({ type: 'ready' })
  const command = (...parts: string[]) => sendToShell({ type: 'command', command: parts })
  const rateText = (rate: number) => rate ? `${(rate / 1000).toFixed(rate % 1000 ? 1 : 0)} kHz` : ''
</script>

<main>
  <header><h1>WinSqueeze {#if status?.version}<span class="version">{status.version}</span>{/if}</h1><button class="quiet-button" onclick={() => sendToShell({ type: 'openSettings' })}>Settings</button></header>
  {#if status?.updateReady}
    <div class="update-notice">
      <span>WinSqueeze {status.updateReady} is downloaded and ready.</span>
      <button onclick={() => sendToShell({ type: 'applyUpdate' })}>Restart to update</button>
    </div>
  {/if}
  {#if status}
    <section class="card">
      <div class="row"><span class="label">Server</span><span>{status.server || 'Not set'}</span></div>
      <div class="row"><span class="label">Output</span><span>{status.device || '—'}{status.fallback ? ' (fallback)' : ''}</span></div>
      <div class="row"><span class="label">Player</span><span class:warn={status.state !== 'Running'}>{status.message}</span></div>
      {#if status.state === 'Running'}
        <div class="readout">{status.driver}{status.exclusive ? ' EXCLUSIVE' : ' SHARED'}{status.sampleRate ? ` · ${rateText(status.sampleRate)}` : ''}{#if status.exclusive}<span class="badge">bit-perfect path</span>{/if}</div>
      {/if}
    </section>
    <div class="transport">
      <button onclick={() => command('playlist', 'index', '-1')} aria-label="Previous">⏮</button>
      <button class="play" onclick={() => command('pause')} aria-label="Play or pause">⏯</button>
      <button onclick={() => command('playlist', 'index', '+1')} aria-label="Next">⏭</button>
    </div>
    <p class="quiet">Pick something to play from any Lyrion controller and choose this player. Browsing arrives in Phase 3.</p>
  {:else}
    <p class="quiet">Connecting to the shell…</p>
  {/if}
</main>

<style>
  main { padding: 24px; max-width: 640px; }
  header { display: flex; align-items: center; justify-content: space-between; }
  h1 { font-weight: 600; margin: 0 0 16px; }
  .card { background: var(--panel); border-radius: 12px; padding: 16px; }
  .row { display: flex; gap: 12px; padding: 4px 0; }
  .label { color: var(--fg-quiet); width: 64px; flex-shrink: 0; }
  .warn { color: var(--warn); }
  .readout { margin-top: 10px; font-family: var(--font-display); color: var(--readout); text-shadow: 0 0 6px var(--readout-glow); letter-spacing: 0.04em; }
  .badge { margin-left: 10px; font-family: var(--font-ui); font-size: 12px; border: 1px solid var(--readout); border-radius: 10px; padding: 1px 8px; }
  .transport { display: flex; gap: 12px; margin: 20px 0; }
  .transport button { width: 52px; height: 52px; border-radius: 50%; border: 0; background: var(--panel); color: var(--fg); font-size: 20px; cursor: pointer; }
  .transport .play { background: var(--accent); color: #000; }
  .quiet { color: var(--fg-quiet); }
  .version { font-size: 14px; font-weight: 400; color: var(--fg-quiet); margin-left: 8px; }
  .update-notice { display: flex; align-items: center; justify-content: space-between; gap: 12px; background: var(--panel); border: 1px solid var(--accent); border-radius: 10px; padding: 10px 14px; margin-bottom: 16px; }
  .update-notice button { background: var(--accent); color: #000; border: 0; border-radius: 6px; padding: 6px 12px; cursor: pointer; font-weight: 600; }
  .quiet-button { background: transparent; color: var(--fg-quiet); border: 1px solid var(--panel); border-radius: 6px; padding: 6px 12px; cursor: pointer; }
</style>
