// Lists the English UI text that has no translation in locales/<code>.json (SPEC.md › Languages).
// Finds t('…') / tn(n, '…', '…') in ui/src, L.T("…") / L.N(n, "…", "…") in app/**/*.cs and the fixed text in app/*.xaml.
// node tools/locales.mjs            report missing and unused keys
// node tools/locales.mjs --strict   also fail (exit 1) when any key is missing
// node tools/locales.mjs --missing  print the missing keys as JSON, ready to translate
import fs from 'fs'
import path from 'path'
const root = path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')), '..')
const filesUnder = (folder, extension) => fs.readdirSync(folder, { withFileTypes: true }).flatMap(entry => entry.isDirectory() ? (['node_modules', 'bin', 'obj', 'wwwroot'].includes(entry.name) ? [] : filesUnder(path.join(folder, entry.name), extension)) : entry.name.endsWith(extension) ? [path.join(folder, entry.name)] : [])
const keys = new Map() // English text -> first place it was found
const addKey = (text, place) => { if (/[A-Za-z]{2}/.test(text) && !keys.has(text)) keys.set(text, place) }
const unescapeScript = literal => literal.replace(/\\(u\{?[0-9a-fA-F]+\}?|x[0-9a-fA-F]{2}|.)/g, (escape, code) => code === 'n' ? '\n' : code === 't' ? '\t' : /^u/.test(code) ? String.fromCodePoint(parseInt(code.replace(/[u{}]/g, ''), 16)) : /^x/.test(code) ? String.fromCharCode(parseInt(code.slice(1), 16)) : code)
const literal = String.raw`'((?:[^'\\\n]|\\.)*)'|"((?:[^"\\\n]|\\.)*)"|\`((?:[^\`\\$]|\\.)*)\``
const literalText = match => unescapeScript(match[1] ?? match[2] ?? match[3] ?? '')

// Web UI: t('…', …) and tn(count, '…', '…', …)
for (const file of filesUnder(path.join(root, 'ui/src'), '.ts').concat(filesUnder(path.join(root, 'ui/src'), '.svelte'))) {
  const source = fs.readFileSync(file, 'utf8').replace(/^\s*\/\/.*$/gm, ''), place = path.relative(root, file) // whole-line comments hold examples
  for (const match of source.matchAll(new RegExp(String.raw`(?<![\w.$])t\(\s*(?:${literal})`, 'g'))) addKey(literalText(match.slice(0)), place)
  for (const match of source.matchAll(new RegExp(String.raw`(?<![\w.$])tn\([^,()]*(?:\([^()]*\))?[^,()]*,\s*(?:${literal})\s*,\s*(?:${literal})`, 'g'))) { addKey(unescapeScript(match[1] ?? match[2] ?? match[3] ?? ''), place); addKey(unescapeScript(match[4] ?? match[5] ?? match[6] ?? ''), place) }
}
// Shell: L.T("…") and L.N(count, "…", "…")
const csString = String.raw`"((?:[^"\\\n]|\\.)*)"`
for (const file of filesUnder(path.join(root, 'app'), '.cs')) {
  const source = fs.readFileSync(file, 'utf8').replace(/^\s*\/\/.*$/gm, ''), place = path.relative(root, file)
  for (const match of source.matchAll(new RegExp(String.raw`\bL\.T\(\s*${csString}`, 'g'))) addKey(unescapeScript(match[1]), place)
  for (const match of source.matchAll(new RegExp(String.raw`\bL\.N\([^,]+,\s*${csString}\s*,\s*${csString}`, 'g'))) { addKey(unescapeScript(match[1]), place); addKey(unescapeScript(match[2]), place) }
}
// XAML: titles, labels, buttons, tooltips and headers, and the text runs of mixed TextBlocks (what L.Translate translates)
const xmlText = text => text.replace(/&#x([0-9a-fA-F]+);/g, (entity, code) => String.fromCodePoint(parseInt(code, 16))).replace(/&#(\d+);/g, (entity, code) => String.fromCodePoint(Number(code))).replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&')
for (const file of filesUnder(path.join(root, 'app'), '.xaml')) {
  const source = fs.readFileSync(file, 'utf8').replace(/<!--[\s\S]*?-->/g, ''), place = path.relative(root, file)
  const templates = source.replace(/<(DataTemplate|ControlTemplate|Style)\b[\s\S]*?<\/\1>/g, '') // not in the logical tree, so never translated
  for (const match of templates.matchAll(/\s(?:Text|Content|Header|ToolTip|Title)="([^"{][^"]*)"/g)) addKey(xmlText(match[1]), place)
  for (const match of templates.matchAll(/<TextBlock\b[^>]*?(?<!\/)>([\s\S]*?)<\/TextBlock>/g)) for (const run of match[1].replace(/<[^>]+>/g, '\u0000').split('\u0000')) if (run.trim()) addKey(xmlText(run.replace(/\s+/g, ' ')), place)
}

const strict = process.argv.includes('--strict'), printMissing = process.argv.includes('--missing')
let missingTotal = 0
for (const localeFile of fs.readdirSync(path.join(root, 'locales')).filter(name => name.endsWith('.json'))) {
  const dictionary = JSON.parse(fs.readFileSync(path.join(root, 'locales', localeFile), 'utf8'))
  const missing = [...keys.keys()].filter(key => !dictionary[key])
  const unused = Object.keys(dictionary).filter(key => !keys.has(key))
  missingTotal += missing.length
  if (printMissing) { console.log(JSON.stringify(Object.fromEntries(missing.map(key => [key, ''])), null, 1)); continue }
  console.log(`${localeFile}: ${keys.size - missing.length} of ${keys.size} translated, ${missing.length} missing, ${unused.length} not found in the code (built at run time, or no longer used)`)
  for (const key of missing) console.log(`${process.env.GITHUB_ACTIONS ? '::warning::' : '  '}missing in ${localeFile}: ${JSON.stringify(key)}  (${keys.get(key)})`) // a warning on the run's summary in CI
}
if (strict && missingTotal > 0) { console.log('Add the missing text to locales/*.json'); process.exit(1) }
