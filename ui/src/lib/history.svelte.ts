// Play history (SPEC.md › Play history): the shell records plays; this bumps whenever it adds or removes some
import { onShellMessage } from '../bridge'
export const history = $state({ version: 0 })
onShellMessage('historyChanged', () => history.version++)
