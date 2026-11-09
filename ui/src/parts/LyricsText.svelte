<script lang="ts">
  import { command } from '../bridge'
  import { app } from '../state.svelte'
  import { playPosition, lineAt, versesOf, bookText, type TimedLine } from '../lib/lyrics'
  // Lyrics set as text: timed ones follow the song (the sung line lit and kept in view, a click on a line jumps there); plain ones as verses
  let { lyrics, stage = false }: { lyrics: any, stage?: boolean } = $props()
  const lines = $derived<TimedLine[]>(Array.isArray(lyrics?.events) ? lyrics.events : [])
  const timed = $derived(lines.length > 1)
  let now = $state(Date.now())
  $effect(() => { if (!timed) return; const ticker = setInterval(() => now = Date.now(), 100); return () => clearInterval(ticker) })
  const current = $derived(timed && app.nowPlaying ? lineAt(lines, playPosition(now)) : -1)
  // Kept in view about a third of the way down, unless the reader has just scrolled away (they get it back a few seconds later)
  let list = $state<HTMLElement>(), readerScrolledAt = 0
  const scrollerOf = (element: HTMLElement) => { let parent = element.parentElement; while (parent && !/(auto|scroll)/.test(getComputedStyle(parent).overflowY)) parent = parent.parentElement; return parent ?? document.scrollingElement as HTMLElement }
  $effect(() => {
    const index = current; if (!list || index < 0 || Date.now() - readerScrolledAt < 4000) return
    const line = list.querySelectorAll<HTMLElement>('.line')[index], scroller = scrollerOf(list); if (!line) return
    const lineBox = line.getBoundingClientRect(), scrollerBox = scroller.getBoundingClientRect()
    scroller.scrollTo({ top: scroller.scrollTop + lineBox.top - scrollerBox.top - scroller.clientHeight * (stage ? 0.4 : 0.34) + lineBox.height / 2, behavior: 'smooth' })
  })
  const readerScrolled = () => readerScrolledAt = Date.now()
</script>

<svelte:window onkeydown={(event) => { if (['ArrowUp', 'ArrowDown', 'PageUp', 'PageDown', 'Home', 'End'].includes(event.key)) readerScrolled() }} />
{#if timed}
  <div class="lyrics-text timed" class:stage bind:this={list} onwheel={readerScrolled}>
    {#each lines as line, index}
      {#if line.text}<button class="line" class:sung={index < current} class:now={index === current} onclick={() => command('time', Math.max(0, line.t - 0.2).toFixed(1))} title="Play from here">{bookText(line.text)}</button>
      {:else}<span class="line gap" class:now={index === current} aria-hidden="true"></span>{/if}
    {/each}
  </div>
{:else}
  <div class="lyrics-text" class:stage>{#each versesOf(lyrics?.text) as verse}<p>{#each verse as line, index}{#if index}<br />{/if}{bookText(line)}{/each}</p>{/each}</div>
{/if}

<style>
  /* A book's face rather than the UI's: Newsreader (optical sizes, so it suits both the tab and full screen), set loose with ragged lines */
  .lyrics-text { font-family: var(--font-text); font-size: 17.5px; line-height: 1.5; font-optical-sizing: auto; font-weight: 400; letter-spacing: 0.003em; text-wrap: pretty; font-variant-numeric: oldstyle-nums proportional-nums; font-feature-settings: "kern", "liga", "clig"; user-select: text; padding: 4px 0 0; }
  .lyrics-text p { margin: 0 0 1.05em; }
  .timed { display: flex; flex-direction: column; align-items: flex-start; }
  .line { display: block; background: none; border: 0; padding: 0.14em 0; margin: 0; color: inherit; font: inherit; text-align: left; cursor: pointer; opacity: 0.42; transition: opacity 0.35s ease, font-weight 0.35s ease; }
  .line:hover { opacity: 0.75; }
  .line.sung { opacity: 0.6; }
  .line.now { opacity: 1; font-weight: 560; }
  .line.gap { height: 0.7em; cursor: default; }
  /* Full screen: big type, the sung line bright and the rest a quiet wash */
  .stage { font-size: clamp(26px, 3.4vw, 58px); line-height: 1.28; letter-spacing: -0.006em; padding: 0; }
  .stage .line { padding: 0.17em 0; opacity: 0.26; }
  .stage .line.sung { opacity: 0.4; }
  .stage .line.now { opacity: 1; font-weight: 520; }
  .stage .line:hover:not(.now) { opacity: 0.55; }
  .stage p { margin: 0 0 1.1em; opacity: 0.92; }
  @media (prefers-reduced-motion: reduce) { .line { transition: none; } }
</style>
