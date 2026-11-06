<script lang="ts">
  // Crisp transport icons as SVG: text glyphs (⏮ ▶ ⏭) render off-centre with colour fringes in WebView2
  let { name, size = 18 }: { name: 'back' | 'history' | 'forward' | 'previous' | 'next' | 'play' | 'pause' | 'volume' | 'speaker' | 'pin' | 'favourites' | 'artists' | 'genres' | 'composers' | 'performers' | 'folders' | 'albums' | 'queue' | 'plus' | 'settings' | 'playlists' | 'verified' | 'grid-large' | 'grid-small' | 'list' | 'cards' | 'minimise' | 'maximise' | 'restore' | 'close' | 'download' | 'radio' | 'apps'; size?: number } = $props()
</script>

<svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true" class="icon">
  {#if name === 'back'}<path d="M14.5 5.5L8 12l6.5 6.5" class="stroke" />
  {:else if name === 'forward'}<path d="M9.5 5.5L16 12l-6.5 6.5" class="stroke" />
  {:else if name === 'play'}<path d="M8 5.14v13.72a1 1 0 0 0 1.52.85l10.6-6.86a1 1 0 0 0 0-1.7L9.52 4.29A1 1 0 0 0 8 5.14z" />
  {:else if name === 'pause'}<rect x="6.5" y="5" width="4" height="14" rx="1.2" /><rect x="13.5" y="5" width="4" height="14" rx="1.2" />
  {:else if name === 'previous'}<rect x="5" y="5.5" width="2.6" height="13" rx="1" /><path d="M19 6.4v11.2a.9.9 0 0 1-1.4.75L9.4 12.75a.9.9 0 0 1 0-1.5l8.2-5.6A.9.9 0 0 1 19 6.4z" />
  {:else if name === 'next'}<rect x="16.4" y="5.5" width="2.6" height="13" rx="1" /><path d="M5 6.4v11.2a.9.9 0 0 0 1.4.75l8.2-5.6a.9.9 0 0 0 0-1.5L6.4 5.65A.9.9 0 0 0 5 6.4z" />
  {:else if name === 'history'}<path d="M4.5 12a7.5 7.5 0 1 0 2.2-5.3M4.5 4.5v3.2h3.2" class="stroke" /><path d="M12 8v4.2l2.8 1.8" class="stroke" />
  {:else if name === 'volume'}<path d="M4 9.5h3.2L11.5 6v12l-4.3-3.5H4z" /><path d="M15 9a4.2 4.2 0 0 1 0 6M17.6 6.6a7.6 7.6 0 0 1 0 10.8" class="stroke" />
  {:else if name === 'speaker'}<rect x="6" y="3.5" width="12" height="17" rx="2.5" class="stroke" /><circle cx="12" cy="14.5" r="3" class="stroke" /><circle cx="12" cy="7.8" r="1.2" />
  {:else if name === 'pin'}<path d="M9 4h6l-1 6 3 3H7l3-3zM12 13v7" class="stroke" />
  {:else if name === 'favourites'}<path d="M12 4.2l2.3 4.7 5.2.75-3.75 3.65.9 5.15L12 16l-4.65 2.45.9-5.15L4.5 9.65l5.2-.75z" class="stroke" />
  {:else if name === 'artists'}<circle cx="12" cy="8.5" r="3.6" class="stroke" /><path d="M5 19.5c.8-3.6 3.6-5.5 7-5.5s6.2 1.9 7 5.5" class="stroke" />
  {:else if name === 'genres'}<path d="M4 12.6V5.5A1.5 1.5 0 0 1 5.5 4h7.1l7.4 7.4-8.6 8.6z" class="stroke" /><circle cx="8.5" cy="8.5" r="1.4" />
  {:else if name === 'composers'}<path d="M9 18V6l10-2v12" class="stroke" /><circle cx="6.5" cy="18" r="2.5" class="stroke" /><circle cx="16.5" cy="16" r="2.5" class="stroke" />
  {:else if name === 'performers'}<path d="M5 19l9-9M13 6.5l4.5 4.5M15.5 4l4.5 4.5-2.5 2.5L13 6.5z" class="stroke" />
  {:else if name === 'folders'}<path d="M3.5 7.5A1.5 1.5 0 0 1 5 6h4.5l2 2H19a1.5 1.5 0 0 1 1.5 1.5v8A1.5 1.5 0 0 1 19 19H5a1.5 1.5 0 0 1-1.5-1.5z" class="stroke" />
  {:else if name === 'albums'}<circle cx="12" cy="12" r="8" class="stroke" /><circle cx="12" cy="12" r="2.2" class="stroke" />
  {:else if name === 'queue'}<path d="M4 6.5h16M4 12h16M4 17.5h10" class="stroke" />
  {:else if name === 'plus'}<path d="M12 5v14M5 12h14" class="stroke" />
  {:else if name === 'settings'}<circle cx="12" cy="12" r="3" class="stroke" /><path d="M12 3.5v2.2M12 18.3v2.2M3.5 12h2.2M18.3 12h2.2M6 6l1.6 1.6M16.4 16.4L18 18M6 18l1.6-1.6M16.4 7.6L18 6" class="stroke" />
  {:else if name === 'playlists'}<path d="M4 6.5h11M4 11h11M4 15.5h6" class="stroke" /><path d="M17 13v6.5" class="stroke" /><circle cx="15.5" cy="19.5" r="1.8" class="stroke" /><path d="M17 13l3-1" class="stroke" />
  {:else if name === 'verified'}<path d="M20.20 12.00Q22.35 16.29 17.80 17.80Q16.29 22.35 12.00 20.20Q7.71 22.35 6.20 17.80Q1.65 16.29 3.80 12.00Q1.65 7.71 6.20 6.20Q7.71 1.65 12.00 3.80Q16.29 1.65 17.80 6.20Q22.35 7.71 20.20 12.00z" /><path d="M8.3 12.2l2.5 2.5 4.9-5" class="stroke tick" />
  {:else if name === 'grid-large'}<rect x="4.5" y="4.5" width="6.5" height="6.5" rx="1.3" class="stroke" /><rect x="13" y="4.5" width="6.5" height="6.5" rx="1.3" class="stroke" /><rect x="4.5" y="13" width="6.5" height="6.5" rx="1.3" class="stroke" /><rect x="13" y="13" width="6.5" height="6.5" rx="1.3" class="stroke" />
  {:else if name === 'grid-small'}{#each [5, 10.5, 16] as gridY}{#each [5, 10.5, 16] as gridX}<rect x={gridX} y={gridY} width="3.5" height="3.5" rx=".8" />{/each}{/each}
  {:else if name === 'cards'}<rect x="4" y="4.5" width="16" height="6.5" rx="1.6" class="stroke" /><rect x="4" y="13" width="16" height="6.5" rx="1.6" class="stroke" /><rect x="6" y="6.3" width="3" height="3" rx=".5" /><rect x="6" y="14.8" width="3" height="3" rx=".5" />
  {:else if name === 'list'}<path d="M9 6.5h11M9 12h11M9 17.5h11" class="stroke" /><circle cx="5" cy="6.5" r="1.2" /><circle cx="5" cy="12" r="1.2" /><circle cx="5" cy="17.5" r="1.2" />
  {:else if name === 'minimise'}<path d="M6 12h12" class="stroke thin" />
  {:else if name === 'maximise'}<rect x="6.5" y="6.5" width="11" height="11" rx="1.5" class="stroke thin" />
  {:else if name === 'restore'}<rect x="6" y="8.5" width="9.5" height="9.5" rx="1.5" class="stroke thin" /><path d="M9 8.5V7.5A1.5 1.5 0 0 1 10.5 6h6A1.5 1.5 0 0 1 18 7.5v6a1.5 1.5 0 0 1-1.5 1.5h-1" class="stroke thin" />
  {:else if name === 'close'}<path d="M7 7l10 10M17 7L7 17" class="stroke thin" />
  {:else if name === 'download'}<path d="M12 4.5v10M7.5 10.5L12 15l4.5-4.5M5 19h14" class="stroke" />
  {:else if name === 'radio'}<rect x="3.5" y="8.5" width="17" height="11" rx="2" class="stroke" /><path d="M7 8.5l9-4.5" class="stroke" /><circle cx="15.5" cy="14" r="2.4" class="stroke" /><path d="M6.5 12.5h4M6.5 15.5h4" class="stroke" />
  {:else if name === 'apps'}<rect x="4" y="4" width="6.5" height="6.5" rx="1.6" class="stroke" /><rect x="13.5" y="4" width="6.5" height="6.5" rx="1.6" class="stroke" /><rect x="4" y="13.5" width="6.5" height="6.5" rx="1.6" class="stroke" /><rect x="13.5" y="13.5" width="6.5" height="6.5" rx="1.6" class="stroke" />{/if}
</svg>

<style>
  .icon { display: block; fill: currentColor; }
  .stroke.tick { stroke: var(--on-accent, #fff); stroke-width: 2.2; }
  .thin { stroke-width: 1.3; }
  .stroke { fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; }
</style>
