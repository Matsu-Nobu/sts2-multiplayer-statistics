import type { SessionDoc } from './types';

/** HTTP の状態コードを持つエラー (404 なら「見つかりません」の画面にする)。 */
export class HttpError extends Error {
  constructor(message: string, readonly status: number) { super(message); }
}

export interface FetchResult {
  doc: SessionDoc | null;        // null = 304 (no change)
  etag: string | null;
}

export async function fetchSession(id: string, prevEtag: string | null): Promise<FetchResult> {
  const headers: Record<string, string> = {};
  if (prevEtag) headers['If-None-Match'] = prevEtag;

  const res = await fetch(`/api/sessions/${encodeURIComponent(id)}`, { headers });

  if (res.status === 304) {
    return { doc: null, etag: prevEtag };
  }
  if (!res.ok) {
    throw new HttpError(`fetch session ${id}: ${res.status} ${res.statusText}`, res.status);
  }
  const doc = (await res.json()) as SessionDoc;
  const etag = res.headers.get('ETag');
  return { doc, etag };
}
