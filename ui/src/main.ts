import { mount } from 'svelte'
import App from './App.svelte'
import EqualiserPanel from './EqualiserPanel.svelte'
import './tokens.css'
// The EQ window loads the same bundle with #eq
export default mount(location.hash === '#eq' ? EqualiserPanel : App, { target: document.getElementById('app')! })
