// UI text in the user's language (SPEC.md › Languages). The English text is the key; locales/<code>.json maps it to the translation, and anything missing stays English.
// The shell picks the language (Windows' display language or Settings › General › Language) and passes it as ?lang=
import german from '../../../locales/de.json'
const dictionaries: Record<string, Record<string, string>> = { de: german }
export const language = new URLSearchParams(location.search).get('lang') ?? 'en'
const dictionary = dictionaries[language] ?? {}
if (typeof document !== 'undefined') document.documentElement.lang = language
// t('Show all {count}', { count }) fills {names} after translating
export function t(english: string, values?: Record<string, unknown>) {
  const text = dictionary[english] || english
  return values ? text.replace(/\{(\w+)\}/g, (placeholder, name) => name in values ? String(values[name]) : placeholder) : text
}
// Singular or plural by count, both forms being keys: tn(albums, '{count} album', '{count} albums')
export const tn = (count: number, one: string, other: string, values?: Record<string, unknown>) => t(count === 1 ? one : other, Object.assign({ count }, values))
