export class ApiError extends Error {
  constructor(public readonly status: number, public readonly code: string) { super(code); }
}

// No persistent browser storage: credentials remain in the protected server cookie.
let csrf: string | undefined;
let identityGeneration = 0;
const requests = new Set<AbortController>();
export function clearIdentity() {
  identityGeneration++;
  csrf = undefined;
  for (const request of requests) request.abort();
  requests.clear();
}

export async function api<T>(path: string, options: { method?: string; body?: unknown; signal?: AbortSignal } = {}): Promise<T> {
  if (!path.startsWith('/') || path.startsWith('//')) throw new Error('API paths must be local');
  const generation = identityGeneration;
  const method = options.method ?? 'GET';
  if (method !== 'GET' && !csrf) {
    const token = await api<{ token: string }>('/session/csrf');
    if (generation !== identityGeneration) throw new DOMException('Identity changed', 'AbortError');
    csrf = token.token;
  }
  const controller = new AbortController();
  requests.add(controller);
  const abort = () => controller.abort();
  options.signal?.addEventListener('abort', abort, { once: true });
  if (options.signal?.aborted) controller.abort();
  try {
    const response = await fetch('/api/v1' + path, {
      method, credentials: 'same-origin', cache: 'no-store', redirect: 'error', signal: controller.signal,
      headers: { Accept: 'application/json', ...(options.body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...(method === 'GET' ? {} : { 'X-CSRF-TOKEN': csrf! }) },
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
    });
    if (generation !== identityGeneration) throw new DOMException('Identity changed', 'AbortError');
    if (!response.ok) {
      const problem = await response.json().catch(() => ({})) as { code?: string; error?: string; title?: string };
      if (response.status === 401) { clearIdentity(); window.dispatchEvent(new Event('sonda:session-expired')); }
      throw new ApiError(response.status, problem.code ?? problem.error ?? problem.title ?? `http_${response.status}`);
    }
    return response.status === 204 ? undefined as T : await response.json() as T;
  } finally {
    requests.delete(controller);
    options.signal?.removeEventListener('abort', abort);
  }
}

export type PendingCommand<T> = Readonly<{ operationId: string; path: string; method: string; body: T & { operationId: string } }>;
export function command<T extends object>(path: string, body: T, method = 'POST'): PendingCommand<T> {
  const operationId = crypto.randomUUID();
  return { operationId, path, method, body: { ...structuredClone(body), operationId } };
}
// Caller retains this exact command for retries. Never generate another ID on a network error.
export function submit<T>(pending: PendingCommand<object>) {
  return api<T>(pending.path, { method: pending.method, body: pending.body });
}

