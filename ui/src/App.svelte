<script lang="ts">
  import { sendToShell, onShellMessage } from './bridge'
  let lastReply = $state('')
  onShellMessage((message) => { lastReply = JSON.stringify(message) })
</script>

<main>
  <h1>WinSqueeze</h1>
  <p class="quiet">Shell scaffold. Browsing arrives in Phase 3.</p>
  <button onclick={() => sendToShell({ type: 'ping' })}>Ping shell</button>
  {#if lastReply}<pre>{lastReply}</pre>{/if}
</main>

<style>
  main { padding: 24px; }
  .quiet { color: var(--fg-quiet); }
  button { background: var(--accent); border: 0; border-radius: 6px; padding: 8px 14px; color: #000; cursor: pointer; }
</style>
