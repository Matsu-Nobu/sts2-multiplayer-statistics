/** 表示の言葉 (spec ui.md §4.3: 日本語に揃える)。 */

export const CARD_TYPE_LABEL: Record<string, string> = {
  Attack: 'アタック', Skill: 'スキル', Power: 'パワー', Status: '状態異常', Curse: '呪い', Quest: 'クエスト',
  Potion: 'ポーション', Relic: 'レリック', Orb: 'オーブ',
};
export const cardTypeLabel = (t: string | undefined | null) => (t ? CARD_TYPE_LABEL[t] ?? t : '');

export const BADGE_RARITY: Record<string, { label: string; color: string }> = {
  Bronze: { label: '銅', color: '#d08b5b' },
  Silver: { label: '銀', color: '#cbd5e1' },
  Gold:   { label: '金', color: '#facc15' },
};

export const OUTCOME_LABEL: Record<string, string> = { victory: '勝利', death: '死亡', abandoned: '放棄' };
