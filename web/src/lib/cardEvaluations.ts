// カード報酬の AI の評価を、階の詳細に出す形にする (spec run-overview.md「AI の評価」)。
import type { CardRewardEvaluation } from './types';

/** 表示しているプレイヤーのこの階の評価を、開いた順に返す。同じカード報酬を開き直したときは、最後に開いたときの評価だけを残す。 */
export function floorCardEvaluations(evals: CardRewardEvaluation[], playerId: string, floor: number): CardRewardEvaluation[] {
  const latest = new Map<string, CardRewardEvaluation>();
  for (const e of evals) {
    if (e.player_id !== playerId || e.floor !== floor) continue;
    // 提示されたカードの並びが同じ評価を、同じカード報酬とみなす
    const key = e.offered.map(c => `${c.card_id}+${c.upgrade_level}`).join(',');
    latest.delete(key);
    latest.set(key, e);
  }
  return [...latest.values()].sort((a, b) => a.event_id - b.event_id);
}

/** 確率を % で書く。小数 1 桁、0.1% 未満か 99.9% より大きいときは小数 2 桁。 */
export function formatProb(p: number): string {
  const pct = p * 100;
  return `${pct.toFixed(pct < 0.1 || pct > 99.9 ? 2 : 1)}%`;
}

// ゲームのカード報酬の画面の表記 (ローカライズの card_reward_ui の OPTION_<ID>.name)
const ALTERNATIVE_LABELS: Record<string, string> = { SKIP: 'スキップ', REROLL: 'リロール', SACRIFICE: '捧げる' };
export const alternativeLabel = (id: string) => ALTERNATIVE_LABELS[id.toUpperCase()] ?? id;

const STATUS_TEXT: Record<Exclude<CardRewardEvaluation['status'], 'ok'>, string> = {
  unknown_card: 'AI が学習していないカードがあるため、評価していません',
  unsupported_version: 'このゲームのバージョンには、AI が対応していません',
  unsupported_character: 'このキャラクターには、AI が対応していません',
  invalid_payload: '評価できませんでした',
};
export const statusText = (s: Exclude<CardRewardEvaluation['status'], 'ok'>) => STATUS_TEXT[s];

export const EVALUATION_HELP =
  'カード報酬を開いたときに、AI（AlphaDefect）がそれぞれの選択肢を選ぶ確率です。AI は 1 つを強く選ぶ傾向があり、100% 近い数字は、ほかの選択肢が悪いという意味ではありません。';
