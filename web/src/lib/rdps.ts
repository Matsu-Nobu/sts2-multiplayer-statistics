/**
 * Skada 風 rDPS（ダメージ貢献度）算出。
 *
 * 基準は「有効ダメージ」= 敵 HP に通った分 + 敵 block を削った分（amount + blocked_damage）。
 * シールド削りも貢献として扱う。
 *
 * 各 damage_dealt イベントについて:
 *   - 通常ダメ + Vulnerable on target → 1/3 を Vulnerable applier に stacks 加重で配分
 *   - 相手に付けたデバフ (毒・Doom 等) によるダメージ (source_appliers あり) → 100% を付与者にスタック比で配分
 *     (毒はターン開始時の発動もカード効果からの発動も同じ。与ダメージ集計と同じ splitByStacks で按分)
 *
 * 複数プレイヤーが同じデバフを撒いた場合、各 applier の stacks 比で按分する。
 * stacks 情報が無い旧 payload では `applier` 単独に全額を帰属（後方互換）。
 */

import type { EventRecord, DamageDealtPayload, PowerSnapshot } from './types';
import { latestCombatEvents, splitByStacks, sharedAppliers } from './aggregate';
import { isModsV2, stepContributions, stepActors, stepLabel, allocateInt, type ModsV2 } from './contrib';

export interface RdpsBreakdown {
  total: number;          // self + to の合計
  self: number;
  from: { source: string; applier: string; amount: number }[];
  to:   { source: string; recipient: string; amount: number }[];
}

export interface RdpsTable {
  byPlayer: Record<string, RdpsBreakdown>;
}

export function computeRdps(events: EventRecord[]): RdpsTable {
  events = latestCombatEvents(events);
  const byPlayer: Record<string, RdpsBreakdown> = {};
  const ensure = (pid: string): RdpsBreakdown => {
    if (!byPlayer[pid]) byPlayer[pid] = { total: 0, self: 0, from: [], to: [] };
    return byPlayer[pid];
  };
  const credit = (recipient: string, applier: string, source: string, amount: number) => {
    if (amount <= 0) return;
    if (recipient === applier) ensure(recipient).self += amount;
    else {
      ensure(recipient).from.push({ source, applier, amount });
      ensure(applier).to.push({ source, recipient, amount });
    }
  };

  for (const ev of events) {
    if (ev.event_type !== 'damage_dealt') continue;
    const p = ev.payload as DamageDealtPayload;
    const dealer = ev.player_id;
    if (!dealer) continue;
    // 「有効ダメージ」基準で計算: HP に通った分 + 敵 block を削った分 (= amount + blocked = total_damage)。
    // ゲームの DamageResult.TotalDamage は BlockedDamage + UnblockedDamage で、超過分 (overkill) を含まない
    // (デコンパイル確認済 v0.111.0)。以前は total - overkill としていて、とどめの一撃で超過分を二重に引いていた。
    const amount = p.amount ?? 0;
    const blocked = p.blocked_damage ?? 0;
    const effective = amount + blocked;
    if (effective <= 0) continue;

    // 1. 相手に付けたデバフ (毒・Doom 等) によるダメージ: source_appliers の付与者にスタック比で 100%
    //    (与ダメージ集計 aggregate.splitSharedDamage と同じ按分 → 欄によって数字がずれない)
    const shared = sharedAppliers(p);
    if (shared && shared.length > 0) {
      for (const s of splitByStacks(effective, shared)) credit(s.player_id, s.player_id, p.is_doom_kill ? 'doom' : (p.source_card_id ?? 'debuff').replace(/_POWER$/i, '').toLowerCase(), s.share);
      continue;
    }
    // (旧データ: source_appliers が無い毒・Doom は active_on_target の内訳で按分)
    if (p.source_card_id === 'POISON_POWER') {
      distributeByStacks(p.active_on_target, 'POISON_POWER', effective, dealer, 'poison', credit);
      continue;
    }
    if (p.source_card_id === 'DOOM_POWER' || p.is_doom_kill) {
      distributeByStacks(p.active_on_target, 'DOOM_POWER', effective, dealer, 'doom', credit);
      continue;
    }

    // 2. v2: 補正 1 つずつの内訳 (spec combat-stats.md §3.5)
    if (isModsV2(p.modifications)) {
      attributeV2(p.modifications, dealer, effective, credit);
      continue;
    }
    // 旧データ: (pre, post, modifier) の記録で按分 / それも無ければ弱体 1/3 の固定ルール
    if (Array.isArray(p.modifications) && p.modifications.length > 0) {
      attributeViaModifications(p, dealer, effective, credit);
    } else {
      const vulnContrib = Math.round(effective / 3);
      let dealerShare = effective;
      const vulnSnap = findPower(p.active_on_target, 'VULNERABLE_POWER');
      if (vulnSnap) {
        const distributed = distributeAmongAppliers(vulnSnap, vulnContrib, dealer, 'vulnerable', credit);
        dealerShare -= distributed;
      }
      credit(dealer, dealer, 'self', dealerShare);
    }
  }

  for (const pid of Object.keys(byPlayer)) {
    const b = byPlayer[pid];
    b.total = b.self + b.to.reduce((s, x) => s + x.amount, 0);
  }
  return { byPlayer };
}

/**
 * Hook.ModifyDamage で観測した (pre, post, modifier_ids[]) ログを使って attribution する。
 *
 * total 値（modifications.post の最終値）と effective の比でスケールしてから配分する。
 * オーバーキルや block 削りで効きが減衰している分も effective 側で吸収される。
 *
 * 不変条件: Σ(全 credit) ≈ effective を保つ。各 delta は以下のように扱う:
 *   - delta > 0 + 該当 power（target / dealer どちら側でも） + applier あり
 *       → stacks 加重で applier に配分
 *   - delta > 0 + applier 不明 (relic / Enchant / passive)
 *       → dealer の self に積む
 *   - delta < 0 (Intangible cap、WEAK on dealer 等のダメ減算)
 *       → applier に credit はしない（rDPS は正値のみ）。dealer self から減算して invariant を保つ
 */
function attributeViaModifications(
  p: import('./types').DamageDealtPayload,
  dealer: string,
  effective: number,
  credit: (recipient: string, applier: string, source: string, amt: number) => void,
): void {
  const mods = Array.isArray(p.modifications) ? p.modifications : [];
  const finalPost = mods.length > 0 ? mods[mods.length - 1].post : (p.total_damage ?? effective);
  const scale = finalPost > 0 ? effective / finalPost : 1;

  // target / dealer どちらにも修飾源が居うる (VULN は target、STR/WEAK は dealer)
  const findInBoth = (id: string): PowerSnapshot | null =>
    findPower(p.active_on_target, id) ?? findPower(p.active_on_dealer, id);

  // 起点（カードの基礎ダメ）= 最初の modification の pre
  const basePre = mods.length > 0 ? mods[0].pre : 0;
  let dealerShare = basePre;     // total-damage 空間で蓄積

  for (const m of mods) {
    const delta = m.post - m.pre;
    if (delta === 0) continue;

    // 負方向修飾 (Intangible cap、WEAK on dealer 等) は applier に正の credit を出せない。
    // 不変条件 (Σ credit ≈ effective) を保つため dealer self から減算して終わり。
    if (delta < 0) {
      dealerShare += delta;
      continue;
    }

    const matchedSnaps = m.modifier_ids
      .map(id => findInBoth(id))
      .filter((s): s is PowerSnapshot => s != null);

    if (matchedSnaps.length === 0) {
      // 該当 power 無し (relic / card Enchant 等) → dealer self に積む
      dealerShare += delta;
      continue;
    }

    // 同フェーズに複数 power が居る場合は均等分割（近似）
    const perSnap = delta / matchedSnaps.length;
    for (const snap of matchedSnaps) {
      const appliers = snap.appliers && snap.appliers.length > 0
        ? snap.appliers
        : (snap.applier ? [{ player_id: snap.applier, stacks: snap.stacks }] : []);

      if (appliers.length === 0) {
        // applier 不明 (passive enemy buff 等) → dealer self に積んで invariant 維持
        dealerShare += perSnap;
        continue;
      }

      const label = snap.power_id.replace(/_POWER$/i, '').toLowerCase();
      // applier への配分は effective 空間へ scale してから
      distributeAmongAppliers(snap, Math.round(perSnap * scale), dealer, label, credit);
    }
  }

  credit(dealer, dealer, 'self', Math.round(dealerShare * scale));
}

/**
 * v2: 1 ヒットの有効ダメージ E を、基礎ダメージ (本人) と補正ごとの寄与 (行為者) に配る。
 * 正の寄与は行為者へ (本人が付けたものは本人)、負の寄与は本人の取り分から引く (0 未満にしない)。
 * 最後に全体を E に合わせて縮める → 合計は必ず E (spec combat-stats.md §3.5)。
 */
function attributeV2(
  m: ModsV2,
  dealer: string,
  effective: number,
  credit: (recipient: string, applier: string, source: string, amt: number) => void,
): void {
  const contribs = stepContributions(m).filter(c => c.step.phase !== 'hp_lost');
  const SELF = `${dealer}\u0000self`;
  const weights = new Map<string, number>();
  const add = (k: string, w: number) => weights.set(k, (weights.get(k) ?? 0) + w);
  const base = m.base ?? ((m.final ?? effective) - contribs.reduce((a, c) => a + c.contrib, 0));
  add(SELF, base);
  for (const { step, contrib } of contribs) {
    const actors = stepActors(step);
    if (contrib > 0 && actors.length > 0) {
      for (const a of actors) add(a.player_id === dealer ? SELF : `${a.player_id}\u0000${stepLabel(step)}`, contrib * a.weight);
    } else {
      add(SELF, contrib);   // 行為者のいない正の寄与 / 負の寄与は本人の取り分で調整
    }
  }
  if ((weights.get(SELF) ?? 0) < 0) weights.set(SELF, 0);
  for (const [k, n] of allocateInt(effective, weights)) {
    const [party, label] = k.split('\u0000');
    credit(dealer, party, party === dealer ? 'self' : label, n);
  }
}

function findPower(snap: PowerSnapshot[] | undefined, powerId: string): PowerSnapshot | null {
  if (!snap) return null;
  return snap.find(s => s.power_id === powerId) ?? null;
}

/**
 * power の applier 群に amount を stacks 加重で配分し、`credit` に流す。
 * 戻り値: 実際に配分された合計（rounding により amount と僅差になり得る）。
 * 自分自身が全 stacks を持つ等で fallback できない場合は dealer に self として戻す。
 */
function distributeAmongAppliers(
  snap: PowerSnapshot,
  amount: number,
  dealer: string,
  source: string,
  credit: (recipient: string, applier: string, source: string, amt: number) => void,
): number {
  const appliers = snap.appliers && snap.appliers.length > 0
    ? snap.appliers
    : (snap.applier ? [{ player_id: snap.applier, stacks: snap.stacks }] : []);
  if (appliers.length === 0) return 0;

  const totalStacks = appliers.reduce((s, a) => s + a.stacks, 0);
  if (totalStacks <= 0) return 0;

  // 整数誤差は最後の applier に押し付ける
  let distributed = 0;
  for (let i = 0; i < appliers.length - 1; i++) {
    const share = Math.round(amount * appliers[i].stacks / totalStacks);
    credit(dealer, appliers[i].player_id, source, share);
    distributed += share;
  }
  const last = appliers[appliers.length - 1];
  credit(dealer, last.player_id, source, amount - distributed);
  return amount;
}

/**
 * 間接ダメ用: amount 全額を power の applier 群に「self ダメ」として配分。
 * Poison/Doom は本来 applier の与ダメなので、各 applier に self credit。
 * applier 不明時は dealer に self としてフォールバック。
 */
function distributeByStacks(
  snap: PowerSnapshot[] | undefined,
  powerId: string,
  amount: number,
  dealer: string,
  source: string,
  credit: (recipient: string, applier: string, source: string, amt: number) => void,
): void {
  const power = findPower(snap, powerId);
  const appliers = power?.appliers && power.appliers.length > 0
    ? power.appliers
    : (power?.applier ? [{ player_id: power.applier, stacks: power.stacks }] : []);

  if (appliers.length === 0) {
    credit(dealer, dealer, source, amount);
    return;
  }

  const totalStacks = appliers.reduce((s, a) => s + a.stacks, 0) || 1;
  let distributed = 0;
  for (let i = 0; i < appliers.length - 1; i++) {
    const share = Math.round(amount * appliers[i].stacks / totalStacks);
    credit(appliers[i].player_id, appliers[i].player_id, source, share);
    distributed += share;
  }
  const last = appliers[appliers.length - 1];
  credit(last.player_id, last.player_id, source, amount - distributed);
}
