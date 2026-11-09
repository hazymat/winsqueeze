// UI text in the user's language (SPEC.md › Languages). The English text is the key; locales/<code>.json maps it to the translation, and anything missing stays English.
// The shell picks the language (Windows' display language or Settings › General › Language) and passes it as ?lang=
import german from '../../../locales/de.json'
import ukrainian from '../../../locales/uk.json'
import spanish from '../../../locales/es.json'
import french from '../../../locales/fr.json'
const dictionaries: Record<string, Record<string, string>> = { de: german, uk: ukrainian, es: spanish, fr: french }
export const language = new URLSearchParams(location.search).get('lang') ?? 'en'
const dictionary = dictionaries[language] ?? {}
if (typeof document !== 'undefined') document.documentElement.lang = language
// t('Show all {count}', { count }) fills {names} after translating
export function t(english: string, values?: Record<string, unknown>) {
  let text = dictionary[english] || english
  // "форма1|форма2|форма3": languages with more than two plural forms pick one by {count}
  if (text.includes('|') && typeof values?.count === 'number') { const forms = text.split('|'); text = forms[Math.min(pluralForm(values.count), forms.length - 1)] }
  return values ? text.replace(/\{(\w+)\}/g, (placeholder, name) => name in values ? String(values[name]) : placeholder) : text
}
// Singular or plural by count, both forms being keys: tn(albums, '{count} album', '{count} albums')
export const tn = (count: number, one: string, other: string, values?: Record<string, unknown>) => t(count === 1 ? one : other, Object.assign({ count }, values))
// Which of a translation's "|" forms a count takes: one, few, many (Ukrainian and Polish); other languages have one form
function pluralForm(count: number) {
  const lastDigit = count % 10, lastTwo = count % 100, few = lastDigit >= 2 && lastDigit <= 4 && (lastTwo < 12 || lastTwo > 14)
  return language === 'uk' ? (lastDigit === 1 && lastTwo !== 11 ? 0 : few ? 1 : 2) : language === 'pl' ? (count === 1 ? 0 : few ? 1 : 2) : 0
}
