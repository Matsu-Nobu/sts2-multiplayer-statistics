/** ログインしている人 (spec user-pages.md「ログイン」)。backend の /api/me を 1 回だけ読む。 */

export type ProfileVisibility = 'public' | 'friends' | 'private';
export interface Me { steam_id: string; display_name: string; profile_visibility: ProfileVisibility }

/** ログインしている人とランの関係 (GET /api/sessions/{id}/me。spec ui.md「ランの設定」)。 */
export interface RunRelation { is_participant: boolean; is_recorder: boolean; visibility: 'public' | 'private'; hidden: boolean }

export const auth = $state<{ me: Me | null; loaded: boolean }>({ me: null, loaded: false });

let started = false;

export async function loadMe(): Promise<void> {
  if (started) return;
  started = true;
  const res = await fetch('/api/me');
  auth.me = res.ok ? ((await res.json()) as Me) : null;
  auth.loaded = true;
}

/** Steam のログイン画面へ。ログイン後は今の画面に戻る。 */
export function loginHref(): string {
  return `/auth/steam/login?return=${encodeURIComponent(location.pathname + location.search)}`;
}

export async function logout(): Promise<void> {
  await fetch('/auth/logout', { method: 'POST' });
  location.reload();
}

export const userHref = (steamId: string) => `/u/${encodeURIComponent(steamId)}`;

/** ログインしている人として backend の値を変える (PATCH / DELETE)。失敗したら例外にする。 */
export async function mutate(method: 'PATCH' | 'DELETE', path: string, body?: unknown): Promise<void> {
  const res = await fetch(path, {
    method,
    headers: body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  if (!res.ok) throw new Error(`${method} ${path}: ${res.status}`);
}
