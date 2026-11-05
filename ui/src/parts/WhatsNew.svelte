<script lang="ts">
  import { onShellMessage, sendToShell } from '../bridge'
  // In the mini player the card can't fit: a pulsing dot instead, which slides down a one-line notice; Details opens the full window and the card
  let { mini = false, offset = false }: { mini?: boolean; offset?: boolean } = $props()
  let noticeOpen = $state(false)
  // After an update restart: a small glass card with the notes of every version since the one you had
  let whatsNew = $state<{ fromVersion: string; toVersion: string; releases: { version: string; date: string; notes: string }[] } | null>(null)
  onShellMessage('whatsNew', message => whatsNew = { fromVersion: message.fromVersion, toVersion: message.toVersion, releases: message.releases })
  const lines = (notes: string) => notes.split('\n').map(line => line.trim()).filter(line => line && !/^[A-Z][a-z]+(-[A-Za-z]+)+: /.test(line)) // drop git trailers (Key-Name: value)
</script>

{#if whatsNew && mini}
  <button class="whats-dot" class:offset onclick={() => noticeOpen = !noticeOpen} aria-label="What's new" title="Updated to {whatsNew.toVersion}: what's new"></button>
  <div class="mini-notice" class:open={noticeOpen}><span>Updated to {whatsNew.toVersion}</span><button class="primary" onclick={() => { noticeOpen = false; sendToShell({ type: 'windowMode', mode: 'desktop' }) }}>Details</button><button class="ghost" onclick={() => whatsNew = null} aria-label="Dismiss">×</button></div>
{:else if whatsNew}
  <div class="whats-new" role="dialog" aria-label="What's new">
    <header><div><h3>What's new</h3><span class="versions">{whatsNew.fromVersion} → {whatsNew.toVersion}</span></div><button class="dismiss" onclick={() => whatsNew = null} aria-label="Dismiss">×</button></header>
    <div class="releases">
      {#each whatsNew.releases as release (release.version)}
        <section><div class="release-head"><b>{release.version}</b><span>{release.date}</span></div>
          <ul>{#each lines(release.notes) as line}<li>{line.replace(/^[-*]\s*/, '')}</li>{:else}<li class="quiet">Notes for this version are on the <a href="https://github.com/hazymat/winsqueeze/releases/tag/v{release.version}" target="_blank" rel="noreferrer">releases page</a>.</li>{/each}</ul>
        </section>
      {/each}
    </div>
    <button class="ok" onclick={() => whatsNew = null}>Got it</button>
  </div>
{/if}

<style>
  .whats-new { position: fixed; z-index: 90; right: 18px; top: 64px; width: min(380px, calc(100vw - 36px)); max-height: min(460px, calc(100vh - 140px)); display: flex; flex-direction: column; border-radius: 14px; padding: 14px 16px 12px; background: var(--panel-solid); backdrop-filter: blur(28px) saturate(1.4); /* frosted but nearly opaque: the list behind must never read through the text */ border: 1px solid var(--line); box-shadow: 0 18px 50px var(--shadow), inset 0 1px 0 rgba(255, 255, 255, 0.12); animation: arrive 220ms ease-out; }
  @keyframes arrive { from { opacity: 0; transform: translateY(-6px); } }
  header { display: flex; align-items: flex-start; justify-content: space-between; gap: 10px; }
  h3 { margin: 0; font-size: 16px; }
  .versions { color: var(--fg-quiet); font-size: 12px; }
  .dismiss { background: none; border: 0; color: var(--fg-quiet); font-size: 20px; line-height: 1; cursor: pointer; padding: 0 2px; } .dismiss:hover { color: var(--fg); }
  .releases { overflow-y: auto; margin: 10px -4px 10px 0; padding-right: 4px; display: flex; flex-direction: column; gap: 10px; }
  .release-head { display: flex; justify-content: space-between; font-size: 12px; color: var(--fg-quiet); } .release-head b { color: var(--fg); }
  ul { margin: 4px 0 0; padding-left: 18px; font-size: 13px; line-height: 1.4; } li + li { margin-top: 3px; }
  .quiet { color: var(--fg-quiet); } .quiet a { color: var(--accent); }
  .ok { align-self: flex-end; background: var(--accent); color: var(--on-accent); border: 0; border-radius: 8px; padding: 6px 14px; font: inherit; cursor: pointer; }
  .whats-dot { position: fixed; top: 5px; right: 5px; z-index: 30; width: 9px; height: 9px; padding: 0; border: 0; border-radius: 50%; background: var(--accent); cursor: pointer; animation: whats-pulse 1.8s ease-in-out infinite; } .whats-dot.offset { right: 20px; }
  .whats-dot::before { content: ""; position: absolute; inset: -6px; border-radius: 50%; }
  @keyframes whats-pulse { 0%, 100% { box-shadow: 0 0 0 0 color-mix(in srgb, var(--accent) 70%, transparent); } 60% { box-shadow: 0 0 0 7px color-mix(in srgb, var(--accent) 0%, transparent); } }
  .mini-notice { position: fixed; left: 0; right: 0; top: 0; z-index: 31; display: flex; align-items: center; gap: 10px; padding: 10px 12px; font-size: 13px; background: var(--panel-solid); box-shadow: 0 4px 14px rgba(0, 0, 0, 0.35); transform: translateY(-110%); transition: transform 260ms cubic-bezier(0.2, 0.8, 0.2, 1); }
  .mini-notice.open { transform: translateY(0); } .mini-notice span { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  @media (prefers-reduced-motion: reduce) { .whats-new { animation: none; } }
</style>
