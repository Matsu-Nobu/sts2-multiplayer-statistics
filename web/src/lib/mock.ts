import type {
  SessionDoc, EventRecord, SessionMeta, PlayerMeta,
  RunStartPayload, RunEndPayload, CombatStartPayload, CombatEndPayload,
  CardPlayedPayload, CardDrawnPayload, DamageDealtPayload, DamageReceivedPayload,
  BlockGainedPayload, PowerChangedPayload, EnergySpentPayload, PowerSnapshot,
} from './types';

/**
 * 2人プレイのラン: HOST（アイアンクラッド）+ ALLY（サイレント、毒持ち）。
 * 戦闘 1: Cultist        (HOST 主体、ALLY が Vulnerable をかける) — 勝
 * 戦闘 2: Lagavulin      (ALLY 毒戦法、HOST タンク) — 勝
 * 戦闘 3: Slime Boss     (HOST 攻撃メイン) — 負
 */

const HOST = '76561199204788207';
const ALLY = '76561198801229872';

const NAMES: Record<string, string> = {
  [HOST]: 'Nobu',
  [ALLY]: 'Friend',
};

// === ビルダー ===============================================================

let evCounter = 0;
function nextUuid() {
  evCounter++;
  return `mock-${String(evCounter).padStart(6, '0')}`;
}

function baseTime(offsetSec: number): string {
  const d = new Date('2026-05-05T12:00:00Z').getTime() + offsetSec * 1000;
  return new Date(d).toISOString();
}

interface TurnCtx {
  combatIndex: number;
  turnNumber: number;
  floor: number;
  seq: number;
  timeOffset: number;        // 単調増加するシード
  events: EventRecord[];
}

function emit<P>(ctx: TurnCtx, eventType: string, playerId: string | null, payload: P, withTurn: boolean): void {
  const ev: EventRecord<P> = {
    event_uuid: nextUuid(),
    event_type: eventType,
    occurred_at: baseTime(ctx.timeOffset++),
    player_id: playerId ?? undefined,
    floor: ctx.floor,
    combat_index: ctx.combatIndex,
    payload,
  };
  if (withTurn) {
    ev.turn_number = ctx.turnNumber;
    ev.sequence = ctx.seq++;
  }
  ctx.events.push(ev);
}

// === 戦闘ジェネレータ =======================================================

interface DamageSpec {
  player: string;
  amount: number;
  card: string;
  cardName: string;
  cardType: string;
  active_on_target?: PowerSnapshot[];
}

function genTurn(ctx: TurnCtx, draws: { player: string; cards: { id: string; name: string }[] }[],
                actions: ({
                  type: 'card_played'; player: string; cardId: string; cardName: string; cardType: string;
                } | {
                  type: 'damage_dealt'; player: string; amount: number; cardId: string; cardName: string; cardType: string;
                  active_on_target?: PowerSnapshot[];
                } | {
                  type: 'block_gained'; player: string; amount: number; cardId: string; from?: string;
                } | {
                  type: 'power_changed'; applier: string; powerId: string; delta: number; cardId: string;
                } | {
                  type: 'energy_spent'; player: string; amount: number; cardId: string;
                } | {
                  type: 'damage_received'; player: string; amount: number;
                })[]): void {
  // ターン頭のドロー
  for (const d of draws) {
    for (const c of d.cards) {
      emit<CardDrawnPayload>(ctx, 'card_drawn', d.player, { card_id: c.id, card_name: c.name, from_hand_draw: false }, true);
    }
  }
  // アクション
  for (const a of actions) {
    switch (a.type) {
      case 'card_played':
        emit<CardPlayedPayload>(ctx, 'card_played', a.player, {
          card_id: a.cardId, card_name: a.cardName, card_type: a.cardType, target_creature_id: 'enemy:0',
        }, true);
        break;
      case 'damage_dealt':
        emit<DamageDealtPayload>(ctx, 'damage_dealt', a.player, {
          amount: a.amount, target_creature_id: 'enemy:0', source_card_id: a.cardId,
          source_card_name: a.cardName, source_card_type: a.cardType, hit_index: 0,
          active_on_target: a.active_on_target ?? [],
          active_on_dealer: [],
        }, true);
        break;
      case 'block_gained':
        emit<BlockGainedPayload>(ctx, 'block_gained', a.player, {
          amount: a.amount, source_card_id: a.cardId, from_player: a.from ?? a.player,
        }, true);
        break;
      case 'power_changed':
        emit<PowerChangedPayload>(ctx, 'power_changed', a.applier, {
          power_id: a.powerId, delta: a.delta,
          target_creature_id: 'enemy:0', target_player_id: null, source_card_id: a.cardId,
        }, true);
        break;
      case 'energy_spent':
        emit<EnergySpentPayload>(ctx, 'energy_spent', a.player, { amount: a.amount, source_card_id: a.cardId }, true);
        break;
      case 'damage_received':
        emit<DamageReceivedPayload>(ctx, 'damage_received', a.player, {
          amount: a.amount, source_creature_id: 'enemy:0',
        }, true);
        break;
    }
  }
}

// === 全体組み立て ===========================================================

export function mockSession(): SessionDoc {
  evCounter = 0;
  const events: EventRecord[] = [];
  let timeOffset = 0;

  // run_start
  events.push({
    event_uuid: nextUuid(), event_type: 'run_start',
    occurred_at: baseTime(timeOffset++),
    player_id: HOST, floor: 0,
    payload: { character_id: 'IRONCLAD', ascension: 5, seed: 'MOCK1234' } as RunStartPayload,
  });

  function combat(idx: number, encounterId: string, encounterName: string, roomType: 'Monster' | 'Elite' | 'Boss',
                  victory: boolean, body: (ctx: TurnCtx) => void) {
    events.push({
      event_uuid: nextUuid(), event_type: 'combat_start',
      occurred_at: baseTime(timeOffset++),
      floor: idx, combat_index: idx,
      payload: { combat_index: idx, encounter_id: encounterId, encounter_name: encounterName, room_type: roomType } as CombatStartPayload,
    });
    const ctx: TurnCtx = { combatIndex: idx, turnNumber: 1, floor: idx, seq: 0, timeOffset, events };
    body(ctx);
    timeOffset = ctx.timeOffset;
    events.push({
      event_uuid: nextUuid(), event_type: 'combat_end',
      occurred_at: baseTime(timeOffset++),
      floor: idx, combat_index: idx,
      payload: { combat_index: idx, victory } as CombatEndPayload,
    });
  }

  // === 戦闘 1: Cultist （HOST 主体、ALLY が Vulnerable）====================
  combat(1, 'CULTIST', 'Cultist', 'Monster', true, ctx => {
    // Turn 1
    genTurn(ctx,
      [
        { player: HOST, cards: [{ id: 'STRIKE_R', name: 'ストライク' }, { id: 'DEFEND_R', name: 'ディフェンド' }, { id: 'BASH', name: 'バッシュ' }] },
        { player: ALLY, cards: [{ id: 'STRIKE_G', name: 'ストライク' }, { id: 'NEUTRALIZE', name: 'ニュートラライズ' }] },
      ],
      [
        { type: 'energy_spent',  player: ALLY, amount: 0, cardId: 'NEUTRALIZE' },
        { type: 'card_played',   player: ALLY, cardId: 'NEUTRALIZE', cardName: 'ニュートラライズ', cardType: 'Attack' },
        { type: 'power_changed', applier: ALLY, powerId: 'WEAK_POWER', delta: 1, cardId: 'NEUTRALIZE' },
        { type: 'damage_dealt',  player: ALLY, amount: 3, cardId: 'NEUTRALIZE', cardName: 'ニュートラライズ', cardType: 'Attack' },
        { type: 'energy_spent',  player: HOST, amount: 2, cardId: 'BASH' },
        { type: 'card_played',   player: HOST, cardId: 'BASH', cardName: 'バッシュ', cardType: 'Attack' },
        { type: 'power_changed', applier: HOST, powerId: 'VULNERABLE_POWER', delta: 2, cardId: 'BASH' },
        { type: 'damage_dealt',  player: HOST, amount: 8, cardId: 'BASH', cardName: 'バッシュ', cardType: 'Attack' },
        { type: 'energy_spent',  player: HOST, amount: 1, cardId: 'STRIKE_R' },
        { type: 'card_played',   player: HOST, cardId: 'STRIKE_R', cardName: 'ストライク', cardType: 'Attack' },
        { type: 'damage_dealt',  player: HOST, amount: 9, cardId: 'STRIKE_R', cardName: 'ストライク', cardType: 'Attack',
          active_on_target: [{ power_id: 'VULNERABLE_POWER', stacks: 2, applier: HOST }] },
      ],
    );
    ctx.turnNumber++; ctx.seq = 0;
    // Turn 2 — ALLY が Vulnerable をかけ HOST が殴る（rDPS で ALLY に貢献が乗るシナリオ）
    genTurn(ctx,
      [
        { player: HOST, cards: [{ id: 'STRIKE_R', name: 'ストライク' }, { id: 'IRON_WAVE', name: 'アイアンウェーブ' }] },
        { player: ALLY, cards: [{ id: 'BANE', name: '猛毒の刃' }, { id: 'DEFEND_G', name: 'ディフェンド' }] },
      ],
      [
        { type: 'damage_received', player: HOST, amount: 6 },
        { type: 'energy_spent',    player: ALLY, amount: 1, cardId: 'BANE' },
        { type: 'card_played',     player: ALLY, cardId: 'BANE', cardName: '猛毒の刃', cardType: 'Attack' },
        { type: 'power_changed',   applier: ALLY, powerId: 'POISON_POWER', delta: 6, cardId: 'BANE' },
        { type: 'damage_dealt',    player: ALLY, amount: 7, cardId: 'BANE', cardName: '猛毒の刃', cardType: 'Attack',
          active_on_target: [{ power_id: 'POISON_POWER', stacks: 6, applier: ALLY }, { power_id: 'VULNERABLE_POWER', stacks: 1, applier: HOST }] },
        { type: 'energy_spent',    player: HOST, amount: 1, cardId: 'IRON_WAVE' },
        { type: 'card_played',     player: HOST, cardId: 'IRON_WAVE', cardName: 'アイアンウェーブ', cardType: 'Attack' },
        { type: 'block_gained',    player: HOST, amount: 5, cardId: 'IRON_WAVE' },
        { type: 'damage_dealt',    player: HOST, amount: 6, cardId: 'IRON_WAVE', cardName: 'アイアンウェーブ', cardType: 'Attack',
          active_on_target: [{ power_id: 'POISON_POWER', stacks: 6, applier: ALLY }, { power_id: 'VULNERABLE_POWER', stacks: 1, applier: HOST }] },
        { type: 'energy_spent',    player: HOST, amount: 1, cardId: 'STRIKE_R' },
        { type: 'card_played',     player: HOST, cardId: 'STRIKE_R', cardName: 'ストライク', cardType: 'Attack' },
        // ★ HOST のダメージで Vulnerable applier=HOST 自身なので vulnerable contrib なし
        { type: 'damage_dealt',    player: HOST, amount: 9, cardId: 'STRIKE_R', cardName: 'ストライク', cardType: 'Attack',
          active_on_target: [{ power_id: 'POISON_POWER', stacks: 6, applier: ALLY }, { power_id: 'VULNERABLE_POWER', stacks: 1, applier: HOST }] },
        // poison tick: ALLY に 100% 帰属
        { type: 'damage_dealt',    player: ALLY, amount: 6, cardId: '(poison)', cardName: '毒', cardType: 'Power',
          active_on_target: [{ power_id: 'POISON_POWER', stacks: 6, applier: ALLY }] },
      ],
    );
    ctx.turnNumber++; ctx.seq = 0;
    // Turn 3 — 倒す
    genTurn(ctx,
      [
        { player: HOST, cards: [{ id: 'STRIKE_R', name: 'ストライク' }, { id: 'STRIKE_R', name: 'ストライク' }] },
      ],
      [
        { type: 'energy_spent',  player: HOST, amount: 1, cardId: 'STRIKE_R' },
        { type: 'card_played',   player: HOST, cardId: 'STRIKE_R', cardName: 'ストライク', cardType: 'Attack' },
        { type: 'damage_dealt',  player: HOST, amount: 12, cardId: 'STRIKE_R', cardName: 'ストライク', cardType: 'Attack' },
      ],
    );
  });

  // === 戦闘 2: Lagavulin (Elite) ALLY 毒メイン ===
  combat(2, 'LAGAVULIN', 'Lagavulin', 'Elite', true, ctx => {
    genTurn(ctx,
      [
        { player: HOST, cards: [{ id: 'DEFEND_R', name: 'ディフェンド' }, { id: 'DEFEND_R', name: 'ディフェンド' }] },
        { player: ALLY, cards: [{ id: 'BANE', name: '猛毒の刃' }, { id: 'DEADLY_POISON', name: '猛毒' }] },
      ],
      [
        { type: 'card_played',   player: ALLY, cardId: 'DEADLY_POISON', cardName: '猛毒', cardType: 'Skill' },
        { type: 'power_changed', applier: ALLY, powerId: 'POISON_POWER', delta: 5, cardId: 'DEADLY_POISON' },
        { type: 'card_played',   player: ALLY, cardId: 'BANE', cardName: '猛毒の刃', cardType: 'Attack' },
        { type: 'power_changed', applier: ALLY, powerId: 'POISON_POWER', delta: 6, cardId: 'BANE' },
        { type: 'damage_dealt',  player: ALLY, amount: 7, cardId: 'BANE', cardName: '猛毒の刃', cardType: 'Attack',
          active_on_target: [{ power_id: 'POISON_POWER', stacks: 11, applier: ALLY }] },
        { type: 'card_played',   player: HOST, cardId: 'DEFEND_R', cardName: 'ディフェンド', cardType: 'Skill' },
        { type: 'block_gained',  player: HOST, amount: 5, cardId: 'DEFEND_R' },
      ],
    );
    ctx.turnNumber++; ctx.seq = 0;
    genTurn(ctx,
      [],
      [
        { type: 'damage_dealt',  player: ALLY, amount: 11, cardId: '(poison)', cardName: '毒', cardType: 'Power',
          active_on_target: [{ power_id: 'POISON_POWER', stacks: 11, applier: ALLY }] },
        { type: 'damage_dealt',  player: ALLY, amount: 10, cardId: '(poison)', cardName: '毒', cardType: 'Power',
          active_on_target: [{ power_id: 'POISON_POWER', stacks: 10, applier: ALLY }] },
      ],
    );
  });

  // === 戦闘 3: Slime Boss (Boss) — 死亡 ===
  combat(3, 'SLIME_BOSS', 'スライム・ボス', 'Boss', false, ctx => {
    genTurn(ctx,
      [
        { player: HOST, cards: [{ id: 'STRIKE_R', name: 'ストライク' }, { id: 'STRIKE_R', name: 'ストライク' }] },
      ],
      [
        { type: 'damage_received', player: HOST, amount: 24 },
        { type: 'card_played',     player: HOST, cardId: 'STRIKE_R', cardName: 'ストライク', cardType: 'Attack' },
        { type: 'damage_dealt',    player: HOST, amount: 6, cardId: 'STRIKE_R', cardName: 'ストライク', cardType: 'Attack' },
      ],
    );
  });

  // run_end
  events.push({
    event_uuid: nextUuid(), event_type: 'run_end',
    occurred_at: baseTime(timeOffset++),
    player_id: HOST, floor: 3,
    payload: { outcome: 'death', final_floor: 3 } as RunEndPayload,
  });

  const session: SessionMeta = {
    id: 'mock-session',
    created_at: baseTime(0),
    host_name: NAMES[HOST],
    host_steam_id: HOST,
    character_id: 'IRONCLAD',
    ascension: 5,
    seed: 'MOCK1234',
    outcome: 'death',
    final_floor: 3,
    finished_at: baseTime(timeOffset),
  };
  const players: PlayerMeta[] = [
    { steam_id: HOST, display_name: NAMES[HOST] },
    { steam_id: ALLY, display_name: NAMES[ALLY] },
  ];

  return { session, players, events };
}
