// プレイヤーの統計 (spec player-stats.md)。
// 集計はサーバが行い (spec api.md「GET /api/users/{steam_id}/stats」)、web は受け取った数字を表に並べる。

export interface CharacterOption { key: string; name: string; games: number }
export interface VersionOption { key: string; games: number }

// 配列は Act ごと (0 始まり)。offered と picks は提示回数と取った回数、picked_games はその Act で取ったゲームの数、
// cleared はそのうちその Act を突破したゲームの数。games と wins は、どれかの Act で取ったゲームの数とそのうち勝った数。
// total は表の 1 行目に置く「{キャラクター名}全体」の行。
export interface CardRow {
  id: string; name: string; games: number; wins: number;
  offered: number[]; picks: number[]; picked_games: number[]; cleared: number[];
  total?: boolean;
}
export interface RelicRow { id: string; name: string; games: number; wins: number }
export interface EnemyRow { id: string; name: string; room_type: string; fights: number; damage: number; deaths: number }
export interface ChoiceRow { title: string; offered: number; chosen: number; wins: number }
export interface VisitGroup { id: string; name: string; visits: number; rows: ChoiceRow[] }
export interface PartnerRow { steam_id: string; name: string; games: number; wins: number; share_sum: number; share_games: number }

export interface CharacterStats {
  games: number; wins: number; avg_floor: number | null; best_ascension: number | null; longest_streak: number;
  acts: { act: number; rate: number }[];
  death_floors: { floor: number; games: number }[];
  rest: { id: string; count: number }[];
  cards: CardRow[];
  card_total: { games: number; wins: number; picked_games: number[]; cleared: number[] };
  relics: RelicRow[]; enemies: EnemyRow[];
  ancients: VisitGroup[]; events: VisitGroup[];
  solo: { games: number; wins: number }; multi: { games: number; wins: number };
  partners: PartnerRow[];
}

export interface StatsDoc {
  user: { steam_id: string; display_name: string; profile_visibility: string };
  is_me: boolean;
  game_count: number;
  options: { versions: VersionOption[]; modes: string[]; ascensions: number[]; characters: CharacterOption[] };
  character: string | null;
  stats: CharacterStats | null;
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
  group?: string;                             // 2 段の見出しの上の段。続く列で同じ値なら 1 つにまとめる
}

export interface Filters { version: string; mode: string; asc: string; players: string; period: 'all' | '30d' }
export const ALL = '';
// バージョンが記録されていないゲーム (mod の古い版が送ったもの) を選ぶ値
export const UNKNOWN_VERSION = 'unknown';
export const SOLO = 'solo';
export const MULTI = 'multi';

/** API に渡すクエリ。character が null なら、サーバがゲーム数の多いキャラクターを選ぶ。 */
export function statsQuery(character: string | null, f: Filters): string {
  const q = new URLSearchParams();
  if (character != null) q.set('character', character);
  if (f.version !== ALL) q.set('version', f.version);
  if (f.mode !== ALL) q.set('mode', f.mode);
  if (f.asc !== ALL) q.set('ascension', f.asc);
  if (f.players !== ALL) q.set('players', f.players);
  if (f.period !== 'all') q.set('period', f.period);
  const s = q.toString();
  return s ? `?${s}` : '';
}

/** 「{キャラクター名}全体」の行。 */
export function cardTotalRow(st: CharacterStats, name: string): CardRow {
  const zeros = st.card_total.picked_games.map(() => 0);
  return {
    id: '', name: `${name}全体`, games: st.card_total.games, wins: st.card_total.wins, total: true,
    offered: zeros, picks: zeros, picked_games: st.card_total.picked_games, cleared: st.card_total.cleared,
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
