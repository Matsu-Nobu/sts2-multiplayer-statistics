// カタログの dump に、今のカタログにだけある項目を足して書き出す。
// 使い方: node tools/merge-catalog.mjs <dump.json> <今のカタログ.json> <出力先.json>
// ゲームの更新で無くなった項目も、過去のランの tooltip のためにカタログに残す (docs/spec/api.md の GET /catalog.{lang}.json)。
import { existsSync, readFileSync, writeFileSync } from 'node:fs';

const KINDS = ['cards', 'relics', 'potions', 'enchantments', 'rest_site_options'];

const [dumpPath, currentPath, outPath] = process.argv.slice(2);
if (!dumpPath || !currentPath || !outPath) {
  console.error('usage: node tools/merge-catalog.mjs <dump.json> <current.json> <out.json>');
  process.exit(2);
}

const dump = JSON.parse(readFileSync(dumpPath, 'utf8'));
// 新しい言語のカタログを初めて作るときは、引き継ぐ項目が無い。
const current = existsSync(currentPath) ? JSON.parse(readFileSync(currentPath, 'utf8')) : {};

for (const kind of KINDS) {
  const ids = new Set(dump[kind].map(x => x.id));
  const kept = (current[kind] ?? []).filter(x => !ids.has(x.id));
  for (const x of kept) console.log(`  引き継ぎ: ${kind} ${x.id}`);
  dump[kind] = [...dump[kind], ...kept];
}

writeFileSync(outPath, JSON.stringify(dump));
