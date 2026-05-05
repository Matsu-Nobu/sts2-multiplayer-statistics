import type {
  SessionDoc, TurnPayload, EventRecord, PlayerEntry,
  PlayerTurnSummary, PlayerCombatSummary, CardStats,
} from './types';

// 2人プレイのラン: アイアンクラッド + サイレント。3戦闘ぶんを生成。
// 戦闘1: Cultist (3ターン, 勝ち)
// 戦闘2: Slime Boss (5ターン, 勝ち)
// 戦闘3: Lagavulin (4ターン, 負け)

const HOST = '76561199204788207';
const ALLY = '76561198801229872';

const NAMES: Record<string, string> = {
  [HOST]: 'Nobu',
  [ALLY]: 'Friend',
};

interface TurnSpec {
  damage_dealt: number;
  damage_received: number;
  block_gained_self: number;
  energy_used: number;
  cards_played: number;
  cards_drawn: number;
  cards: Array<{ id: string; name: string; type: string; play: number; dmg?: number; block?: number; debuff?: Record<string, number>; max?: number }>;
}

interface CombatSpec {
  index: number;
  encounter_id: string;
  encounter_name: string;
  room_type: 'Monster' | 'Elite' | 'Boss';
  victory: boolean;
  turns: Record<string, TurnSpec[]>; // playerId → per-turn specs
}

const card = (id: string, name: string, type: string, play: number, dmg = 0, max = 0, block = 0, debuff: Record<string, number> = {}) => ({
  id, name, type, play, dmg, max, block, debuff,
});

const COMBATS: CombatSpec[] = [
  {
    index: 1,
    encounter_id: 'CULTIST',
    encounter_name: 'Cultist',
    room_type: 'Monster',
    victory: true,
    turns: {
      [HOST]: [
        { damage_dealt: 12, damage_received: 0, block_gained_self: 5, energy_used: 3, cards_played: 3, cards_drawn: 5,
          cards: [ card('STRIKE_R', 'ストライク', 'Attack', 2, 12, 6), card('DEFEND_R', 'ディフェンド', 'Skill', 1, 0, 0, 5) ] },
        { damage_dealt: 18, damage_received: 6, block_gained_self: 8, energy_used: 3, cards_played: 3, cards_drawn: 5,
          cards: [ card('BASH', 'バッシュ', 'Attack', 1, 8, 8, 0, { 'Vulnerable': 2 }), card('STRIKE_R', 'ストライク', 'Attack', 1, 10, 10), card('DEFEND_R', 'ディフェンド', 'Skill', 1, 0, 0, 8) ] },
        { damage_dealt: 24, damage_received: 0, block_gained_self: 10, energy_used: 3, cards_played: 3, cards_drawn: 5,
          cards: [ card('STRIKE_R', 'ストライク', 'Attack', 2, 24, 12), card('DEFEND_R', 'ディフェンド', 'Skill', 1, 0, 0, 10) ] },
      ],
      [ALLY]: [
        { damage_dealt: 9, damage_received: 0, block_gained_self: 5, energy_used: 3, cards_played: 3, cards_drawn: 5,
          cards: [ card('STRIKE_G', 'ストライク', 'Attack', 1, 6, 6), card('DAGGER_THROW', 'ダガースロー', 'Attack', 1, 3, 3), card('SURVIVOR', 'サバイバー', 'Skill', 1, 0, 0, 5) ] },
        { damage_dealt: 15, damage_received: 5, block_gained_self: 8, energy_used: 3, cards_played: 4, cards_drawn: 5,
          cards: [ card('NEUTRALIZE', 'ニュートラライズ', 'Attack', 1, 3, 3, 0, { 'Weak': 1 }), card('STRIKE_G', 'ストライク', 'Attack', 1, 6, 6), card('DAGGER_THROW', 'ダガースロー', 'Attack', 1, 6, 6), card('SURVIVOR', 'サバイバー', 'Skill', 1, 0, 0, 8) ] },
        { damage_dealt: 12, damage_received: 0, block_gained_self: 5, energy_used: 3, cards_played: 3, cards_drawn: 5,
          cards: [ card('STRIKE_G', 'ストライク', 'Attack', 2, 12, 6), card('SURVIVOR', 'サバイバー', 'Skill', 1, 0, 0, 5) ] },
      ],
    },
  },
  {
    index: 2,
    encounter_id: 'SLIME_BOSS',
    encounter_name: 'Slime Boss',
    room_type: 'Elite',
    victory: true,
    turns: {
      [HOST]: [
        { damage_dealt: 14, damage_received: 0, block_gained_self: 8, energy_used: 3, cards_played: 3, cards_drawn: 5,
          cards: [ card('STRIKE_R', 'ストライク', 'Attack', 2, 14, 7), card('DEFEND_R', 'ディフェンド', 'Skill', 1, 0, 0, 8) ] },
        { damage_dealt: 24, damage_received: 12, block_gained_self: 5, energy_used: 3, cards_played: 3, cards_drawn: 5,
          cards: [ card('BASH', 'バッシュ', 'Attack', 1, 10, 10, 0, { 'Vulnerable': 2 }), card('STRIKE_R', 'ストライク', 'Attack', 1, 14, 14), card('DEFEND_R', 'ディフェンド', 'Skill', 1, 0, 0, 5) ] },
        { damage_dealt: 30, damage_received: 8, block_gained_self: 0, energy_used: 3, cards_played: 4, cards_drawn: 5,
          cards: [ card('STRIKE_R', 'ストライク', 'Attack', 3, 30, 12) ] },
        { damage_dealt: 12, damage_received: 6, block_gained_self: 10, energy_used: 3, cards_played: 3, cards_drawn: 5,
          cards: [ card('STRIKE_R', 'ストライク', 'Attack', 1, 12, 12), card('DEFEND_R', 'ディフェンド', 'Skill', 2, 0, 0, 10) ] },
        { damage_dealt: 18, damage_received: 0, block_gained_self: 0, energy_used: 3, cards_played: 3, cards_drawn: 5,
          cards: [ card('STRIKE_R', 'ストライク', 'Attack', 2, 18, 9), card('CLEAVE', 'クリーブ', 'Attack', 1, 0, 0) ] },
      ],
      [ALLY]: [
        { damage_dealt: 11, damage_received: 0, block_gained_self: 5, energy_used: 3, cards_played: 3, cards_drawn: 5,
          cards: [ card('STRIKE_G', 'ストライク', 'Attack', 2, 11, 6), card('SURVIVOR', 'サバイバー', 'Skill', 1, 0, 0, 5) ] },
        { damage_dealt: 18, damage_received: 0, block_gained_self: 8, energy_used: 3, cards_played: 4, cards_drawn: 5,
          cards: [ card('NEUTRALIZE', 'ニュートラライズ', 'Attack', 1, 3, 3, 0, { 'Weak': 2 }), card('DAGGER_THROW', 'ダガースロー', 'Attack', 2, 12, 6), card('SURVIVOR', 'サバイバー', 'Skill', 1, 0, 0, 8) ] },
        { damage_dealt: 25, damage_received: 4, block_gained_self: 0, energy_used: 4, cards_played: 4, cards_drawn: 5,
          cards: [ card('BACKSTAB', 'バックスタブ', 'Attack', 1, 11, 11), card('DAGGER_THROW', 'ダガースロー', 'Attack', 2, 14, 7), card('SURVIVOR', 'サバイバー', 'Skill', 1, 0, 0, 0) ] },
        { damage_dealt: 9, damage_received: 0, block_gained_self: 5, energy_used: 3, cards_played: 3, cards_drawn: 5,
          cards: [ card('DAGGER_THROW', 'ダガースロー', 'Attack', 2, 9, 5), card('SURVIVOR', 'サバイバー', 'Skill', 1, 0, 0, 5) ] },
        { damage_dealt: 14, damage_received: 0, block_gained_self: 0, energy_used: 3, cards_played: 3, cards_drawn: 5,
          cards: [ card('STRIKE_G', 'ストライク', 'Attack', 1, 6, 6), card('DAGGER_THROW', 'ダガースロー', 'Attack', 1, 8, 8), card('SURVIVOR', 'サバイバー', 'Skill', 1, 0, 0, 0) ] },
      ],
    },
  },
  {
    index: 3,
    encounter_id: 'LAGAVULIN',
    encounter_name: 'Lagavulin',
    room_type: 'Elite',
    victory: false,
    turns: {
      [HOST]: [
        { damage_dealt: 10, damage_received: 0, block_gained_self: 8, energy_used: 3, cards_played: 3, cards_drawn: 5,
          cards: [ card('STRIKE_R', 'ストライク', 'Attack', 1, 10, 10), card('DEFEND_R', 'ディフェンド', 'Skill', 2, 0, 0, 8) ] },
        { damage_dealt: 22, damage_received: 18, block_gained_self: 0, energy_used: 3, cards_played: 3, cards_drawn: 5,
          cards: [ card('BASH', 'バッシュ', 'Attack', 1, 12, 12, 0, { 'Vulnerable': 2 }), card('STRIKE_R', 'ストライク', 'Attack', 1, 10, 10), card('CLEAVE', 'クリーブ', 'Attack', 1, 0, 0) ] },
        { damage_dealt: 28, damage_received: 22, block_gained_self: 0, energy_used: 3, cards_played: 3, cards_drawn: 5,
          cards: [ card('STRIKE_R', 'ストライク', 'Attack', 2, 28, 14), card('CLEAVE', 'クリーブ', 'Attack', 1, 0, 0) ] },
        { damage_dealt: 0, damage_received: 32, block_gained_self: 0, energy_used: 0, cards_played: 0, cards_drawn: 0,
          cards: [] },
      ],
      [ALLY]: [
        { damage_dealt: 12, damage_received: 0, block_gained_self: 5, energy_used: 3, cards_played: 3, cards_drawn: 5,
          cards: [ card('STRIKE_G', 'ストライク', 'Attack', 2, 12, 6), card('SURVIVOR', 'サバイバー', 'Skill', 1, 0, 0, 5) ] },
        { damage_dealt: 20, damage_received: 12, block_gained_self: 0, energy_used: 4, cards_played: 4, cards_drawn: 5,
          cards: [ card('BACKSTAB', 'バックスタブ', 'Attack', 1, 11, 11), card('DAGGER_THROW', 'ダガースロー', 'Attack', 2, 9, 5), card('SURVIVOR', 'サバイバー', 'Skill', 1, 0, 0, 0) ] },
        { damage_dealt: 26, damage_received: 18, block_gained_self: 0, energy_used: 3, cards_played: 3, cards_drawn: 5,
          cards: [ card('NEUTRALIZE', 'ニュートラライズ', 'Attack', 1, 3, 3, 0, { 'Weak': 1 }), card('DAGGER_THROW', 'ダガースロー', 'Attack', 2, 23, 12) ] },
        { damage_dealt: 0, damage_received: 28, block_gained_self: 0, energy_used: 0, cards_played: 0, cards_drawn: 0,
          cards: [] },
      ],
    },
  },
];

function emptyTurn(): PlayerTurnSummary {
  return { damage_dealt: 0, damage_received: 0, block_gained_self: 0, block_given_allies: 0, energy_used: 0, cards_played: 0, cards_drawn: 0, cards: [] };
}
function emptyCombat(): PlayerCombatSummary {
  return { damage_dealt: 0, damage_received: 0, block_gained_self: 0, block_given_allies: 0, energy_used: 0, cards_played: 0, cards_drawn: 0, potions_used: 0, max_single_hit: 0, debuffs_applied: {}, card_stats: [] };
}

function specToCardStats(specs: TurnSpec['cards']): CardStats[] {
  return specs.map(c => ({
    card_id: c.id, card_name: c.name, card_type: c.type,
    play_count: c.play, damage_dealt: c.dmg ?? 0, block_provided: c.block ?? 0,
    debuffs_applied: c.debuff ?? {}, max_single_hit: c.max ?? 0,
  }));
}

function mergeCardStats(a: CardStats[], b: CardStats[]): CardStats[] {
  const map = new Map<string, CardStats>();
  for (const c of a) map.set(c.card_id, { ...c, debuffs_applied: { ...c.debuffs_applied } });
  for (const c of b) {
    const e = map.get(c.card_id);
    if (!e) {
      map.set(c.card_id, { ...c, debuffs_applied: { ...c.debuffs_applied } });
    } else {
      e.play_count += c.play_count;
      e.damage_dealt += c.damage_dealt;
      e.block_provided += c.block_provided;
      e.max_single_hit = Math.max(e.max_single_hit, c.max_single_hit);
      for (const [k, v] of Object.entries(c.debuffs_applied)) e.debuffs_applied[k] = (e.debuffs_applied[k] ?? 0) + v;
    }
  }
  return Array.from(map.values());
}

function buildTurns(): TurnPayload[] {
  const out: TurnPayload[] = [];
  // 各プレイヤーごとに combat 累積を持つ
  const cumByPlayer: Record<string, PlayerCombatSummary> = {};
  for (const c of COMBATS) {
    // この戦闘開始時点で累積はリセット (per-combat 累積)
    for (const pid of Object.keys(c.turns)) cumByPlayer[pid] = emptyCombat();

    const turnCount = Math.max(...Object.values(c.turns).map(t => t.length));
    for (let t = 0; t < turnCount; t++) {
      const players: Record<string, PlayerEntry> = {};
      for (const [pid, specs] of Object.entries(c.turns)) {
        const spec = specs[t];
        const turn: PlayerTurnSummary = spec ? {
          damage_dealt: spec.damage_dealt,
          damage_received: spec.damage_received,
          block_gained_self: spec.block_gained_self,
          block_given_allies: 0,
          energy_used: spec.energy_used,
          cards_played: spec.cards_played,
          cards_drawn: spec.cards_drawn,
          cards: specToCardStats(spec.cards),
        } : emptyTurn();

        const cum = cumByPlayer[pid];
        cum.damage_dealt += turn.damage_dealt;
        cum.damage_received += turn.damage_received;
        cum.block_gained_self += turn.block_gained_self;
        cum.block_given_allies += turn.block_given_allies;
        cum.energy_used += turn.energy_used;
        cum.cards_played += turn.cards_played;
        cum.cards_drawn += turn.cards_drawn;
        cum.max_single_hit = Math.max(cum.max_single_hit, ...turn.cards.map(c => c.max_single_hit), 0);
        for (const c of turn.cards) for (const [k, v] of Object.entries(c.debuffs_applied)) cum.debuffs_applied[k] = (cum.debuffs_applied[k] ?? 0) + v;
        cum.card_stats = mergeCardStats(cum.card_stats, turn.cards);

        players[pid] = {
          player_name: NAMES[pid],
          turn,
          combat: structuredClone(cum),
        };
      }
      out.push({
        combat_index: c.index,
        turn_number: t + 1,
        is_final: t === turnCount - 1,
        timestamp: new Date(Date.now() - (COMBATS.length - c.index) * 600_000 - (turnCount - t) * 30_000).toISOString(),
        players,
      });
    }
  }
  return out;
}

function buildEvents(): EventRecord[] {
  const out: EventRecord[] = [];
  out.push({
    event_uuid: 'evt-run-start',
    event_type: 'run_start',
    occurred_at: new Date(Date.now() - 30 * 60_000).toISOString(),
    player_id: HOST,
    floor: 0,
    payload: { character_id: 'IRONCLAD', ascension: 5, seed: '1234567890' },
  });
  for (const c of COMBATS) {
    out.push({
      event_uuid: `evt-cs-${c.index}`,
      event_type: 'combat_start',
      occurred_at: new Date(Date.now() - (COMBATS.length - c.index + 1) * 600_000).toISOString(),
      player_id: HOST,
      floor: c.index,
      payload: { combat_index: c.index, encounter_id: c.encounter_id, encounter_name: c.encounter_name, room_type: c.room_type },
    });
    out.push({
      event_uuid: `evt-ce-${c.index}`,
      event_type: 'combat_end',
      occurred_at: new Date(Date.now() - (COMBATS.length - c.index) * 600_000).toISOString(),
      player_id: HOST,
      floor: c.index,
      payload: { combat_index: c.index, victory: c.victory },
    });
  }
  // run はまだ終わっていない（final combat は敗北だが run_end は未送信、という想定にしてもいいが見せる用に終了済みに）
  out.push({
    event_uuid: 'evt-run-end',
    event_type: 'run_end',
    occurred_at: new Date(Date.now() - 60_000).toISOString(),
    player_id: HOST,
    floor: 3,
    payload: { outcome: 'death', final_floor: 3 },
  });
  return out;
}

export function mockSession(): SessionDoc {
  return {
    session: {
      id: 'demo',
      created_at: new Date(Date.now() - 30 * 60_000).toISOString(),
      host_name: 'Nobu',
      host_steam_id: HOST,
      character_id: 'IRONCLAD',
      ascension: 5,
      seed: '1234567890',
      outcome: 'death',
      final_floor: 3,
      finished_at: new Date(Date.now() - 60_000).toISOString(),
    },
    players: [
      { steam_id: HOST, display_name: 'Nobu' },
      { steam_id: ALLY, display_name: 'Friend' },
    ],
    turns: buildTurns(),
    events: buildEvents(),
  };
}
