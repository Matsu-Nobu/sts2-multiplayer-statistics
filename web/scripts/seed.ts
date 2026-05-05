// バックエンドにモックデータを流し込んで session URL を出力するスクリプト。
// usage: npx tsx web/scripts/seed.ts [BACKEND_URL]
//   default BACKEND_URL = http://localhost:8080

import { mockSession } from '../src/lib/mock';

const BACKEND_URL = process.argv[2] ?? 'http://localhost:8080';

async function main() {
  const mock = mockSession();

  // 1. POST /sessions
  const createRes = await fetch(`${BACKEND_URL}/sessions`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      host_name:     mock.session.host_name,
      host_steam_id: mock.session.host_steam_id,
      character_id:  mock.session.character_id,
      ascension:     mock.session.ascension,
      seed:          mock.session.seed,
    }),
  });
  if (!createRes.ok) throw new Error(`create session failed: ${createRes.status}`);
  const { session_id, write_token, share_url } = await createRes.json();
  console.log(`created session: ${session_id}`);

  const auth = { 'Content-Type': 'application/json', Authorization: `Bearer ${write_token}` };

  // 2. POST events (bulk) — Phase 3.5 では events 1 本に統合
  const evRes = await fetch(`${BACKEND_URL}/sessions/${session_id}/events`, {
    method: 'POST',
    headers: auth,
    body: JSON.stringify(mock.events),
  });
  if (!evRes.ok) throw new Error(`post events failed: ${evRes.status} ${await evRes.text()}`);
  console.log(`posted ${mock.events.length} events`);

  console.log('');
  console.log(`backend:  ${share_url}`);
  console.log(`web (dev) http://localhost:5173/s/${session_id}`);
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});
