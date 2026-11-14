import { mount } from 'svelte'
import App from './App.svelte'
import EqualiserPanel from './EqualiserPanel.svelte'
import LyricsStage from './LyricsStage.svelte'
import VuPreview from './VuPreview.svelte'
import '@fontsource-variable/newsreader/opsz.css'
import '@fontsource-variable/newsreader/opsz-italic.css'
import './tokens.css'
// The EQ, lyrics and VU Builder windows load the same bundle with #eq, #lyrics and #vu
export default mount(location.hash === '#eq' ? EqualiserPanel : location.hash === '#lyrics' ? LyricsStage : location.hash === '#vu' ? VuPreview : App, { target: document.getElementById('app')! })
