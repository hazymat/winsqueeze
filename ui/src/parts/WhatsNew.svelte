<script lang="ts">
  import { onShellMessage } from '../bridge'
  // After an update restart: a small glass card with the notes of every version since the one you had
  let whatsNew = $state<{ fromVersion: string; toVersion: string; releases: { version: string; date: string; notes: string }[] } | null>(null)
  onShellMessage('whatsNew', message => whatsNew = { fromVersion: message.fromVersion, toVersion: message.toVersion, releases: message.releases })
  const lines = (notes: string) => notes.split('\n').map(line => line.trim()).filter(line => line && !line.startsWith('Co-Authored') && !line.startsWith('Claude-'))
</script>

{#if whatsNew}
  <div class="whats-new" role="dialog" aria-label="What's new">
    <header><div><h3>What's new</h3><span class="versions">{whatsNew.fromVersion} → {whatsNew.toVersion}</span></div><button class="dismiss" onclick={() => whatsNew = null} aria-label="Dismiss">×</button></header>
    <div class="releases">
      {#each whatsNew.releases as release (release.version)}
        <section><div class="release-head"><b>{release.version}</b><span>{release.date}</span></div>
          <ul>{#each lines(release.notes) as line}<li>{line.replace(/^[-*]\s*/, '')}</li>{/each}</ul>
        </section>
      {/each}
    </div>
    <button class="ok" onclick={() => whatsNew = null}>Got it</button>
  </div>
{/if}

<style>
  .whats-new { position: fixed; z-index: 90; right: 18px; top: 64px; width: min(380px, calc(100vw - 36px)); max-height: min(460px, calc(100vh - 140px)); display: flex; flex-direction: column; border-radius: 14px; padding: 14px 16px 12px; background: var(--glass); backdrop-filter: blur(var(--blur)) saturate(1.5); border: 1px solid var(--line); box-shadow: 0 18px 50px var(--shadow), inset 0 1px 0 rgba(255, 255, 255, 0.12); animation: arrive 220ms ease-out; }
  @keyframes arrive { from { opacity: 0; transform: translateY(-6px); } }
  header { display: flex; align-items: flex-start; justify-content: space-between; gap: 10px; }
  h3 { margin: 0; font-size: 16px; }
  .versions { color: var(--fg-quiet); font-size: 12px; }
  .dismiss { background: none; border: 0; color: var(--fg-quiet); font-size: 20px; line-height: 1; cursor: pointer; padding: 0 2px; } .dismiss:hover { color: var(--fg); }
  .releases { overflow-y: auto; margin: 10px -4px 10px 0; padding-right: 4px; display: flex; flex-direction: column; gap: 10px; }
  .release-head { display: flex; justify-content: space-between; font-size: 12px; color: var(--fg-quiet); } .release-head b { color: var(--fg); }
  ul { margin: 4px 0 0; padding-left: 18px; font-size: 13px; line-height: 1.4; } li + li { margin-top: 3px; }
  .ok { align-self: flex-end; background: var(--accent); color: var(--on-accent); border: 0; border-radius: 8px; padding: 6px 14px; font: inherit; cursor: pointer; }
  @media (prefers-reduced-motion: reduce) { .whats-new { animation: none; } }
</style>
