import { live } from '../bridge'
import { openView, toast, back } from '../state.svelte'
import { t } from './i18n'
// Lyrion's player menu format (the one Squeezebox Touch/Radio and iPeng read). Plugins such as Dynamic Playlists publish their menus in it,
// so one browser shows them all natively. An item's action is its own or the page's "base" one, with the item's own params added.
export type MenuAction = { cmd: string[], params?: Record<string, any>, itemsParams?: string, nextWindow?: string }
// Lyrion's own entries (library, favourites, radio, apps, settings) already have WinSqueeze pages
const lyrionCommands = new Set(['browselibrary', 'favorites', 'radios', 'apps', 'myapps', 'globalsearch', 'search', 'playlists', 'musicfolder', 'artists', 'albums', 'genres', 'years', 'selectVirtualLibrary', 'power', 'randomplay', 'jiveplaylists', 'setlinein', 'setdigitalinput', 'alarm', 'alarms', 'recentsearches', 'material-skin', 'jivedummycommand', 'playerpref', 'jiveplayerbrightness'])
export const menuText = (item: any) => String(item.text ?? '').split('\n')
export function actionOf(item: any, name: string, base: any): MenuAction | null {
  const own = item.actions && name in item.actions ? item.actions[name] : base?.actions?.[name]
  if (!own?.cmd) return null
  const params = Object.assign({}, own.params ?? {}, own.itemsParams && item[own.itemsParams] ? item[own.itemsParams] : {})
  return { cmd: own.cmd, params, nextWindow: item.nextWindow ?? own.nextWindow }
}
export const needsWords = (action: MenuAction | null) => !!action && (action.cmd.concat(Object.values(action.params ?? {})).some(part => part === '__TAGGEDINPUT__' || part === '__INPUT__'))
// Lyrion's command array: words, then "key:value" params; typed words replace the input placeholder
export function commandOf(action: MenuAction, words = '', paging = false): unknown[] {
  const fill = (part: any) => part === '__TAGGEDINPUT__' || part === '__INPUT__' ? words : part
  const params = Object.entries(action.params ?? {}).filter(([, value]) => value != null).map(([key, value]) => `${key}:${fill(value)}`)
  return action.cmd.map(fill).concat(paging ? [0, 500] : [], params)
}
export async function browse(action: MenuAction, words = '') { const result = await live(commandOf(action, words, true), true); return { items: (result?.item_loop ?? []) as any[], base: result?.base } }
// Plugin entries in the player's home and My Music menus that Lyrion itself doesn't provide
export async function pluginMenus(appCommands: string[]) {
  const result = await live(['menu', 0, 1000, 'direct:1'], true)
  const pluginCommand = (item: any) => { const command = (actionOf(item, 'go', null) ?? actionOf(item, 'do', null))?.cmd[0] ?? ''; return command && !lyrionCommands.has(command) && !appCommands.includes(command) }
  return ((result?.item_loop ?? []) as any[]).filter(item => !item.isANode && ['home', 'myMusic', 'extras', 'plugins'].includes(item.node) && !String(item.id ?? '').startsWith('myMusic') && pluginCommand(item))
    .sort((first, second) => menuText(first)[0].localeCompare(menuText(second)[0]))
}
// Choosing an entry: a plain "go" opens its page; "do" (or a "go" that names where to go next) runs it and reports back
export async function openMenuItem(item: any, base: any): Promise<string> {
  const title = menuText(item)[0], run = actionOf(item, 'do', base), go = actionOf(item, 'go', base), next = item.nextWindow ?? run?.nextWindow ?? go?.nextWindow ?? ''
  if (!run && go && !next || needsWords(run ?? go)) { openView({ kind: 'menu', key: JSON.stringify(run ?? go), title }); return 'opened' }
  await live(commandOf((run ?? go)!), true)
  toast(item.style === 'itemplay' || next === 'nowPlaying' ? t('Playing {items}', { items: title }) : t('Done'))
  if (next === 'parent' || next === 'grandparent') back()
  return next
}
