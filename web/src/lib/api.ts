import type { SessionDoc } from './types';

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
    throw new Error(`fetch session ${id}: ${res.status} ${res.statusText}`);
  }
  const doc = (await res.json()) as SessionDoc;
  const etag = res.headers.get('ETag');
  return { doc, etag };
}
