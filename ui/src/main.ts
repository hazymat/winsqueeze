import { mount } from 'svelte'
import App from './App.svelte'
import EqualiserPanel from './EqualiserPanel.svelte'
import LyricsStage from './LyricsStage.svelte'
import '@fontsource-variable/newsreader/opsz.css'
import '@fontsource-variable/newsreader/opsz-italic.css'
import './tokens.css'
// The EQ and lyrics windows load the same bundle with #eq and #lyrics
export default mount(location.hash === '#eq' ? EqualiserPanel : location.hash === '#lyrics' ? LyricsStage : App, { target: document.getElementById('app')! })
