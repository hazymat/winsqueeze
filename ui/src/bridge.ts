// WebView2 message bridge to the C# shell. Outside WebView2 (vite dev) messages are logged only.
type ShellMessage = { type: string; [key: string]: unknown }
const webview = (window as any).chrome?.webview
export function sendToShell(message: ShellMessage) { if (webview) webview.postMessage(message); else console.log('[bridge]', message) }
export function onShellMessage(handler: (message: ShellMessage) => void) { webview?.addEventListener('message', (event: MessageEvent) => handler(event.data)) }
