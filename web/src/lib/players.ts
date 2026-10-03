/** プレイヤーの固定色 (doc.players の順。spec ui.md §4.1)。グラフ・比較表・プレイヤー切り替えで同じ色を使う。 */
export const PLAYER_COLORS = ['#7aa2f7', '#bb9af7', '#9ece6a', '#e0af68', '#f7768e'];

export function playerColor(playerIds: string[], pid: string): string {
  const i = playerIds.indexOf(pid);
  return PLAYER_COLORS[(i < 0 ? 0 : i) % PLAYER_COLORS.length];
}
