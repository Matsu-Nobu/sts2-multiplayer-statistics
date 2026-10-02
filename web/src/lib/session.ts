/** セッション画面の共有データ (SessionView が作って context で配る)。 */
import { getContext, setContext } from 'svelte';
import type { SessionDoc, EventRecord } from './types';
import type { CombatInfo, RunTotals } from './aggregate';
import type { CatalogLookup } from './catalog';

export interface SessionCtx {
  readonly doc: SessionDoc;
  readonly combats: CombatInfo[];
  readonly totals: RunTotals;
  readonly playerIds: string[];
  readonly playerNames: Record<string, string>;
  readonly powerNames: Record<string, string>;
  readonly cardNames: Record<string, string>;
  readonly catalog: CatalogLookup | null;
  readonly combatEvents: EventRecord[];                  // 戦闘内 event (やり直し前は除く)
  readonly eventsByCombat: Map<number, EventRecord[]>;
  readonly player: string;                               // 選んでいるプレイヤー
}

const KEY = Symbol('session');
export const setSession = (c: SessionCtx) => setContext(KEY, c);
export const useSession = () => getContext<SessionCtx>(KEY);
