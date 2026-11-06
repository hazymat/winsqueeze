// WebView2 message bridge to the C# shell. Outside WebView2 (vite dev) messages are only logged.
export type ShellMessage = { type: string; [key: string]: any }
const webview = (window as any).chrome?.webview
let nextRequestId = 1
const pendingRequests = new Map<number, (data: any) => void>()
const handlersByType = new Map<string, ((message: ShellMessage) => void)[]>()

webview?.addEventListener('message', (event: MessageEvent) => {
  const message = event.data as ShellMessage
  if (message.type === 'result') { pendingRequests.get(message.id)?.(message.data); pendingRequests.delete(message.id); return }
  for (const handler of handlersByType.get(message.type) ?? []) handler(message)
})

export function sendToShell(message: ShellMessage) { if (webview) webview.postMessage(message); else console.log('[bridge]', message) }
export function onShellMessage(type: string, handler: (message: ShellMessage) => void) { handlersByType.set(type, (handlersByType.get(type) ?? []).concat([handler])) }
export function request<T = any>(message: ShellMessage): Promise<T> {
  const requestId = nextRequestId++
  return new Promise(resolve => { pendingRequests.set(requestId, resolve); sendToShell(Object.assign({ id: requestId }, message)) })
}
export const query = <T = any>(name: string, params: Record<string, unknown> = {}) => request<T>({ type: 'query', name, params })
export const live = <T = any>(command: unknown[], forPlayer = false) => request<T>({ type: 'live', command, player: forPlayer })
export const command = (...parts: unknown[]) => sendToShell({ type: 'command', command: parts })
// Shown wherever the server has no artwork: a plain square (each place rounds its own corners) with a note
const noArtSvg = `<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><rect width='100' height='100' fill='#2a2d34'/><path d='M58 28v32.5a9 9 0 1 1-4-7.5V34l-14 3v27.5a9 9 0 1 1-4-7.5V32z' fill='#6c717c'/></svg>`
export const noArtUrl = 'data:image/svg+xml,' + encodeURIComponent(noArtSvg)
if (typeof document !== 'undefined') document.addEventListener('error', event => { const image = event.target; if (image instanceof HTMLImageElement && image.src.startsWith('https://art.winsqueeze/')) image.src = noArtUrl }, true)
export const artUrl = (coverId: unknown, size = 300) => coverId ? `https://art.winsqueeze/${coverId}/${size}` : ''
// An image the server knows by path or web address (radio and app icons); outside images go through Lyrion's imageproxy, resized
export const serverArt = (source: string, size = 300) => source ? `https://art.winsqueeze/server/${encodeURIComponent(source.startsWith('http') ? `imageproxy/${encodeURIComponent(source)}/image_${size}x${size}_f` : source.replace(/^\//, ''))}` : ''
// A queue item's art: library tracks by coverid; radio and other streams by artwork_url (their coverid is negative and has no cover), however playback was started
export const trackArt = (track: any, size = 300) => { const coverId = String(track?.coverid ?? ''), remoteArt = String(track?.artwork_url ?? ''); return remoteArt && (Number(track?.remote) === 1 || !coverId || coverId.startsWith('-')) ? serverArt(remoteArt, size) : coverId && !coverId.startsWith('-') ? artUrl(coverId, size) : '' }
