// プレイヤーの統計の集計 (spec player-stats.md)。
// backend が返すゲームごとの集計 (spec api.md「GET /api/users/{steam_id}/stats」) を、絞り込んでからキャラクターごとにまとめる。
import type { CatalogLookup } from './catalog';

export interface StatsGame {
  session_id: string; created_at: string;
  character_id: string | null; character_name: string | null; game_mode: string | null; ascension: number | null;
  outcome: 'victory' | 'death'; final_floor: number | null; max_act_index: number;
  partners: { steam_id: string; display_name: string }[];
  damage_share: number | null;
  card_offers: { id: string; name: string; offered: number; picked: number }[];
  relics: { id: string; name: string }[];
  fights: { model_id: string; model_name: string; room_type: string; damage_taken: number; died: boolean }[];
  rest_choices: string[];
  ancients: { model_id: string; model_name: string; options: { title: string; chosen: boolean }[] }[];
  events: { model_id: string; model_name: string; choices: string[] }[];
}

export interface StatsDoc {
  user: { steam_id: string; display_name: string; profile_visibility: string };
  is_me: boolean;
  games: StatsGame[];
}

// 「5 ゲーム未満を隠す」の数 (spec player-stats.md「絞り込み」)
export const MIN_GAMES = 5;

/** 統計の表の列 (StatsTable)。 */
export interface Column<R> {
  label: string;
  num?: boolean;
  sort: (r: R) => number | string | null;   // null は並べ替えの向きによらず最後に置く
  text: (r: R) => string;
  tone?: (r: R) => 'ok' | 'bad' | null;      // 文字色
  href?: (r: R) => string;                    // リンクにする
  badge?: boolean;                            // バッジにする
}

export interface Filters { mode: string; asc: string; players: string; period: 'all' | '30d' }
export const ALL = '';
export const SOLO = 'solo';
export const MULTI = 'multi';

const DAY_MS = 24 * 60 * 60 * 1000;

export const characterKey = (g: StatsGame) => g.character_id ?? '';
export const characterName = (g: StatsGame) => g.character_name ?? g.character_id ?? '—';
export const isWin = (g: StatsGame) => g.outcome === 'victory';

export function applyFilters(games: StatsGame[], f: Filters, now: Date): StatsGame[] {
  const since = now.getTime() - 30 * DAY_MS;
  return games.filter(g =>
    (f.mode === ALL || g.game_mode === f.mode) &&
    (f.asc === ALL || String(g.ascension) === f.asc) &&
    (f.players === ALL || (f.players === MULTI) === (g.partners.length > 0)) &&
    (f.period === 'all' || new Date(g.created_at).getTime() >= since));
}

export interface CharacterOption { key: string; name: string; games: number }

/** キャラクターのドロップダウンの選択肢。絞り込んだ後のゲームにあるキャラクターを、ゲーム数の多い順に並べる。 */
export function characterOptions(games: StatsGame[]): CharacterOption[] {
  const m = new Map<string, CharacterOption>();
  for (const g of games) {
    const k = characterKey(g);
    const t = m.get(k) ?? { key: k, name: characterName(g), games: 0 };
    t.games++;
    m.set(k, t);
  }
  return [...m.values()].sort((a, b) => b.games - a.games || a.name.localeCompare(b.name, 'ja'));
}

export interface CardRow { id: string; name: string; offered: number; picks: number; games: number; wins: number }
export interface RelicRow { id: string; name: string; games: number; wins: number }
export interface EnemyRow { id: string; name: string; roomType: string; fights: number; damage: number; deaths: number }
export interface ChoiceRow { title: string; offered: number; chosen: number; wins: number }
export interface VisitGroup { id: string; name: string; visits: number; rows: ChoiceRow[] }
export interface PartnerRow { steamId: string; name: string; games: number; wins: number; shareSum: number; shareGames: number }

export interface CharacterStats {
  games: number; wins: number; avgFloor: number | null; bestAscension: number | null; longestStreak: number;
  cards: CardRow[]; relics: RelicRow[]; enemies: EnemyRow[];
  acts: { act: number; rate: number }[];
  deathFloors: { floor: number; games: number }[];
  rest: { name: string; count: number }[];
  ancients: VisitGroup[]; events: VisitGroup[];
  solo: { games: number; wins: number }; multi: { games: number; wins: number };
  partners: PartnerRow[];
}

function upsert<T>(m: Map<string, T>, k: string, make: () => T): T {
  let v = m.get(k);
  if (v === undefined) { v = make(); m.set(k, v); }
  return v;
}

/** 1 キャラクターのゲーム (絞り込み済み) から、各区画の数字を出す。 */
export function characterStats(games: StatsGame[], catalog: CatalogLookup | null): CharacterStats {
  const wins = games.filter(isWin).length;
  const floors = games.map(g => g.final_floor).filter((f): f is number => f != null);
  const winAsc = games.filter(isWin).map(g => g.ascension).filter((a): a is number => a != null);

  let streak = 0, longest = 0;
  for (const g of [...games].sort((a, b) => a.created_at.localeCompare(b.created_at))) {
    streak = isWin(g) ? streak + 1 : 0;
    longest = Math.max(longest, streak);
  }

  const cards = new Map<string, CardRow>();
  const relics = new Map<string, RelicRow>();
  const enemies = new Map<string, EnemyRow>();
  const deaths = new Map<number, number>();
  const rest = new Map<string, number>();
  const ancients = new Map<string, VisitGroup>();
  const events = new Map<string, VisitGroup>();
  const partners = new Map<string, PartnerRow>();
  const solo = { games: 0, wins: 0 }, multi = { games: 0, wins: 0 };
  let maxAct = 0;

  for (const g of games) {
    const w = isWin(g) ? 1 : 0;
    for (const c of g.card_offers) {
      const r = upsert(cards, c.id, () => ({ id: c.id, name: catalog?.card(c.id, false)?.name ?? c.name, offered: 0, picks: 0, games: 0, wins: 0 }));
      r.offered += c.offered;
      r.picks += c.picked;
      if (c.picked > 0) { r.games++; r.wins += w; }
    }
    for (const x of g.relics) {
      const r = upsert(relics, x.id, () => ({ id: x.id, name: catalog?.relic(x.id)?.name ?? x.name, games: 0, wins: 0 }));
      r.games++;
      r.wins += w;
    }
    for (const f of g.fights) {
      const r = upsert(enemies, f.model_id, () => ({ id: f.model_id, name: f.model_name || f.model_id, roomType: f.room_type, fights: 0, damage: 0, deaths: 0 }));
      r.fights++;
      r.damage += f.damage_taken;
      if (f.died) r.deaths++;
    }
    if (!isWin(g) && g.final_floor != null) deaths.set(g.final_floor, (deaths.get(g.final_floor) ?? 0) + 1);
    for (const c of g.rest_choices) {
      const name = catalog?.restSiteOption(c)?.name ?? c;
      rest.set(name, (rest.get(name) ?? 0) + 1);
    }
    for (const a of g.ancients) {
      const grp = upsert(ancients, a.model_id, () => ({ id: a.model_id, name: a.model_name || a.model_id, visits: 0, rows: [] }));
      grp.visits++;
      for (const o of a.options) {
        let r = grp.rows.find(x => x.title === o.title);
        if (!r) { r = { title: o.title, offered: 0, chosen: 0, wins: 0 }; grp.rows.push(r); }
        r.offered++;
        if (o.chosen) { r.chosen++; r.wins += w; }
      }
    }
    for (const e of g.events) {
      const grp = upsert(events, e.model_id, () => ({ id: e.model_id, name: e.model_name || e.model_id, visits: 0, rows: [] }));
      grp.visits++;
      for (const c of e.choices) {
        let r = grp.rows.find(x => x.title === c);
        if (!r) { r = { title: c, offered: 0, chosen: 0, wins: 0 }; grp.rows.push(r); }
        r.chosen++;
        r.wins += w;
      }
    }
    const side = g.partners.length > 0 ? multi : solo;
    side.games++;
    side.wins += w;
    for (const p of g.partners) {
      const r = upsert(partners, p.steam_id, () => ({ steamId: p.steam_id, name: p.display_name || p.steam_id, games: 0, wins: 0, shareSum: 0, shareGames: 0 }));
      r.games++;
      r.wins += w;
      if (g.damage_share != null) { r.shareSum += g.damage_share; r.shareGames++; }
    }
    maxAct = Math.max(maxAct, g.max_act_index);
  }

  // Act N 突破 = Act N より先の Act に進んだか、勝ったゲームの割合 (act_index は 0 始まり)
  const acts = games.length === 0 ? [] : Array.from({ length: maxAct + 1 }, (_, i) => ({
    act: i + 1,
    rate: games.filter(g => isWin(g) || g.max_act_index > i).length / games.length,
  }));
  const byVisits = (a: VisitGroup, b: VisitGroup) => b.visits - a.visits || a.name.localeCompare(b.name, 'ja');

  return {
    games: games.length, wins,
    avgFloor: floors.length ? floors.reduce((s, f) => s + f, 0) / floors.length : null,
    bestAscension: winAsc.length ? Math.max(...winAsc) : null,
    longestStreak: longest,
    cards: [...cards.values()], relics: [...relics.values()], enemies: [...enemies.values()],
    acts,
    deathFloors: [...deaths.entries()].map(([floor, n]) => ({ floor, games: n })).sort((a, b) => a.floor - b.floor),
    rest: [...rest.entries()].map(([name, count]) => ({ name, count })).sort((a, b) => b.count - a.count),
    ancients: [...ancients.values()].sort(byVisits), events: [...events.values()].sort(byVisits),
    solo, multi, partners: [...partners.values()],
  };
}

/** 勝率 (0〜1)。ゲームが無ければ null。 */
export const rate = (wins: number, games: number): number | null => (games > 0 ? wins / games : null);

/** 割合を整数のパーセントの文字列にする。 */
export const pct = (v: number | null): string => (v == null ? '—' : `${Math.round(v * 100)}%`);

/** 勝率との差 (整数のパーセントの差)。 */
export function deltaPoints(v: number | null, base: number | null): number | null {
  if (v == null || base == null) return null;
  return Math.round((v - base) * 100);
}
