/**
 * power_id を表示用文字列に変換するヘルパ。
 * 1. mod 由来の動的 lookup（events から抽出された PowerModel.Title のローカライズ済み名）が優先
 * 2. それが取れなければ raw な power_id をそのまま返す（勝手に日本語訳しない）
 */
export function formatPowerName(id: string, names: Record<string, string> = {}): string {
  return names[id] ?? id;
}
