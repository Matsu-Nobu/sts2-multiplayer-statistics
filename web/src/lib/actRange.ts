/** ラン全体の貢献度スコアと全戦闘の合計を「Act N まで」に絞る (spec combat-stats.md「Act の範囲」)。 */
import type { EventRecord, FloorSnapshotPayload } from './types';

/** 'all' はラン全体、数字は Act の番号 (0 始まり) で、その Act までの戦闘を数える。 */
export type ActRange = 'all' | `${number}`;

/**
 * 戦闘の階 (combat_index) ごとの Act (0 始まり)。
 * Act は floor_snapshot の act_index から取る。floor_snapshot はその階を出たときに送られる。
 * そのため進行中の戦闘の階には floor_snapshot がまだ無く、その階は直前の floor_snapshot の Act とする。
 * 直前の階がボスの部屋なら、ボスを倒して次の Act に進んでいるので、Act を 1 つ進める。
 */
export function combatActs(events: EventRecord[], combatFloors: number[]): Map<number, number> {
  const snaps = new Map<number, FloorSnapshotPayload>();
  for (const ev of events) {
    if (ev.event_type !== 'floor_snapshot') continue;
    const p = ev.payload as FloorSnapshotPayload;
    snaps.set(p.floor, p);
  }
  const floors = [...snaps.keys()].sort((a, b) => a - b);
  const out = new Map<number, number>();
  for (const f of combatFloors) {
    const own = snaps.get(f);
    if (own) { out.set(f, own.act_index); continue; }
    const prevFloor = floors.filter(x => x < f).pop();
    if (prevFloor == null) { out.set(f, 0); continue; }
    const prev = snaps.get(prevFloor)!;
    out.set(f, prev.act_index + (prev.rooms.some(r => r.room_type === 'Boss') ? 1 : 0));
  }
  return out;
}

/** 選べる範囲。最後の Act までは「全体」と同じなので、それより前の Act と「全体」だけを並べる。 */
export function actRangeOptions(acts: Map<number, number>): { value: ActRange; label: string }[] {
  const distinct = [...new Set(acts.values())].sort((a, b) => a - b);
  return [
    ...distinct.slice(0, -1).map(a => ({ value: `${a}` as ActRange, label: `Act ${a + 1} まで` })),
    { value: 'all', label: '全体' },
  ];
}

export function actRangeLabel(r: ActRange): string {
  return r === 'all' ? 'ラン全体' : `Act ${Number(r) + 1} まで`;
}

export function inActRange(acts: Map<number, number>, floor: number, r: ActRange): boolean {
  return r === 'all' || (acts.get(floor) ?? 0) <= Number(r);
}
