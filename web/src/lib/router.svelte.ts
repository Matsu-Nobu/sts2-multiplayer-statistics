/**
 * 画面の状態を URL に持つ小さなルーター (spec ui.md §1)。
 *
 *   /s/{id}                        ラン全体
 *   /s/{id}/floors                 階の一覧
 *   /s/{id}/floors/{floor}         階の詳細
 *   /s/{id}/combats                戦闘
 *   /s/{id}/combats/{combat_index} 戦闘詳細 (?view=summary|turns|timeline)
 *   共通クエリ ?p={player_id}
 */

export type Page =
  | { name: 'run' }
  | { name: 'floors' }
  | { name: 'floor'; floor: number }
  | { name: 'combats' }
  | { name: 'combat'; combat: number; view: CombatViewName };

export type CombatViewName = 'summary' | 'turns' | 'timeline';

interface Route { sessionId: string | null; isDemo: boolean; page: Page; player: string | null }

function parse(loc: Location): Route {
  const url = new URL(loc.href);
  const isDemo = url.searchParams.has('demo');
  const player = url.searchParams.get('p');
  const m = url.pathname.match(/^\/s\/([^/]+)(?:\/(.*))?$/);
  const sessionId = m ? m[1] : null;
  const rest = (m?.[2] ?? '').replace(/\/+$/, '').split('/').filter(Boolean);
  let page: Page = { name: 'run' };
  if (rest[0] === 'floors') {
    const n = rest[1] != null ? Number(rest[1]) : NaN;
    page = Number.isInteger(n) ? { name: 'floor', floor: n } : { name: 'floors' };
  } else if (rest[0] === 'combats') {
    const n = rest[1] != null ? Number(rest[1]) : NaN;
    const v = url.searchParams.get('view');
    const view: CombatViewName = v === 'turns' || v === 'timeline' ? v : 'summary';
    page = Number.isInteger(n) ? { name: 'combat', combat: n, view } : { name: 'combats' };
  }
  return { sessionId, isDemo: isDemo || !sessionId, page, player };
}

export const route = $state<Route>(parse(window.location));

window.addEventListener('popstate', () => { Object.assign(route, parse(window.location)); });

function pagePath(p: Page): string {
  switch (p.name) {
    case 'run':     return '';
    case 'floors':  return '/floors';
    case 'floor':   return `/floors/${p.floor}`;
    case 'combats': return '/combats';
    case 'combat':  return `/combats/${p.combat}`;
  }
}

/** ページの URL (現在のプレイヤー選択とデモ指定を引き継ぐ)。 */
export function href(p: Page, player: string | null = route.player): string {
  const base = route.sessionId ? `/s/${route.sessionId}` : '/s/demo';
  const q: string[] = [];
  if (route.isDemo) q.push('demo');
  if (player) q.push(`p=${encodeURIComponent(player)}`);
  if (p.name === 'combat' && p.view !== 'summary') q.push(`view=${p.view}`);
  const qs = q.join('&');
  return base + pagePath(p) + (qs ? `?${qs}` : '');
}

/** 画面の移動 (履歴に積む)。replace=true ならプレイヤー・表示の切り替えとして置き換える。 */
export function navigate(p: Page, opts: { replace?: boolean; player?: string | null } = {}) {
  const player = opts.player !== undefined ? opts.player : route.player;
  const url = href(p, player);
  if (opts.replace) history.replaceState(null, '', url); else history.pushState(null, '', url);
  route.page = p;
  route.player = player;
  if (!opts.replace) window.scrollTo(0, 0);
}

/** <a href> のクリックを横取りして画面内で移動する (修飾キー付きは新しいタブ等ブラウザに任せる)。 */
export function link(node: HTMLAnchorElement, p: Page) {
  let page = p;
  const onClick = (e: MouseEvent) => {
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    e.preventDefault();
    navigate(page);
  };
  node.addEventListener('click', onClick);
  return {
    update(np: Page) { page = np; },
    destroy() { node.removeEventListener('click', onClick); },
  };
}
