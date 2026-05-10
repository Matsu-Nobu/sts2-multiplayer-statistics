// カタログ (cards / relics / potions / enchantments の definitions) ロードと整形。
//
// データソース: web/public/catalog.{lang}.json (リポジトリにコミット済の静的ファイル)
// → vite が public/ をそのままコピーするので /catalog.{lang}.json で取得可能。
// → backend は embed FS で同じファイルを `/catalog.{lang}.json` パスでサーブするため、
//    本番でも同 URL が効く。
//
// 更新ワークフロー: STS2 アップデート時のみ `make dump-catalog LANG=ja` で更新。

export type CardEntry = {
  id: string;
  name_base: string;
  name_upgraded: string;
  description_base: string;
  description_upgraded: string;
  rarity: string;
  card_type?: string;
  cost?: number | null;
  max_upgrade?: number;
};

export type RelicEntry = {
  id: string;
  name: string;
  description: string;
  rarity: string;
};

export type PotionEntry = {
  id: string;
  name: string;
  description: string;
  rarity: string;
};

export type EnchantmentEntry = {
  id: string;
  name: string;
  description: string;
};

export interface ItemCatalog {
  schema_version?: number;
  lang?: string;
  generated_at?: string;
  cards: CardEntry[];
  relics: RelicEntry[];
  potions: PotionEntry[];
  enchantments: EnchantmentEntry[];
}

export interface CatalogLookup {
  card(id: string, upgraded?: boolean): { name: string; description: string; rarity: string } | null;
  relic(id: string): RelicEntry | null;
  potion(id: string): PotionEntry | null;
  enchantment(id: string): EnchantmentEntry | null;
}

let _cache: { lang: string; catalog: ItemCatalog; lookup: CatalogLookup } | null = null;
let _inflight: Promise<CatalogLookup> | null = null;

/**
 * カタログ取得 (singleton キャッシュ)。
 * 1 回 fetch → メモリキャッシュ → 以降は同じ Promise を返す。
 */
export async function loadCatalog(lang: string = 'ja'): Promise<CatalogLookup> {
  if (_cache && _cache.lang === lang) return _cache.lookup;
  if (_inflight) return _inflight;

  _inflight = (async () => {
    try {
      const res = await fetch(`/catalog.${lang}.json`, { cache: 'force-cache' });
      if (!res.ok) throw new Error(`catalog fetch failed: ${res.status}`);
      const catalog = (await res.json()) as ItemCatalog;
      const lookup = buildLookup(catalog);
      _cache = { lang, catalog, lookup };
      return lookup;
    } catch (e) {
      console.warn('[catalog] load failed, will retry next call', e);
      // 失敗時は cache に詰めない (次回呼出で再 fetch される)
      const empty: ItemCatalog = { cards: [], relics: [], potions: [], enchantments: [] };
      return buildLookup(empty);
    } finally {
      _inflight = null;
    }
  })();
  return _inflight;
}

function buildLookup(catalog: ItemCatalog): CatalogLookup {
  const cardMap   = new Map(catalog.cards.map(c => [c.id, c]));
  const relicMap  = new Map(catalog.relics.map(r => [r.id, r]));
  const potionMap = new Map(catalog.potions.map(p => [p.id, p]));
  const enchMap   = new Map(catalog.enchantments.map(e => [e.id, e]));
  return {
    card(id, upgraded = false) {
      const c = cardMap.get(id);
      if (!c) return null;
      return {
        name:        upgraded ? c.name_upgraded : c.name_base,
        description: upgraded ? c.description_upgraded : c.description_base,
        rarity:      c.rarity,
      };
    },
    relic: id => relicMap.get(id) ?? null,
    potion: id => potionMap.get(id) ?? null,
    enchantment: id => enchMap.get(id) ?? null,
  };
}

// === 装飾タグ + placeholder のレンダリング ====================================
//
// STS2 LocString のタグ:
//   [gold]X[/gold]    → キーワード強調 (黄)
//   [blue]X[/blue]    → 数値強調 (青)
//   [green]X[/green]  → アップグレード差分強調 (緑)
//   [red]X[/red]      → 警告 (赤)
//   [purple]X[/purple]→ 紫
//   [img]res://...[/img] → アイコン (取り扱い不能 → 削除)
//
// 未解決 placeholder:
//   {Damage:diff()} 等 → 戦闘文脈依存で解決不能 → "XX" 等のフォールバック表示
//
// セキュリティ: catalog のテキストは自分達の dump なので XSS 信頼可。
// だが念のため text を適度に sanitize する (< / > / & を escape) して、その後タグだけ HTML 化。

const TAG_COLOR: Record<string, string> = {
  gold:   'text-yellow-300',
  blue:   'text-sky-400',
  green:  'text-lime-300',
  red:    'text-red-400',
  purple: 'text-purple-400',
};

function escapeHtml(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

/**
 * カタログ由来 description text を tooltip 用 HTML 文字列に変換する。
 * - [color]X[/color] → <span class="text-...">X</span>
 * - [img]...[/img]   → 削除
 * - {Damage:...} 等  → "XX" にフォールバック (戦闘文脈依存で解決不能)
 * - 改行           → <br>
 */
export function renderCardDescription(text: string): string {
  if (!text) return '';
  let s = escapeHtml(text);

  // [img]res://...[/img] は web に対応画像無いので削除
  s = s.replace(/\[img\][^\[]*\[\/img\]/g, '');

  // [color]X[/color] → span
  s = s.replace(/\[(\w+)\]([\s\S]*?)\[\/\1\]/g, (_m, color: string, inner: string) => {
    const cls = TAG_COLOR[color];
    return cls ? `<span class="${cls}">${inner}</span>` : inner;
  });

  // 残った {placeholder...} → "XX" (戦闘文脈依存で解決不能なもの)
  // 例: "{Damage:diff()}ダメージ" → "XXダメージ"
  s = s.replace(/\{[^}]+\}/g, '<span class="text-slate-400">XX</span>');

  // 改行
  s = s.replace(/\n/g, '<br>');

  return s;
}
