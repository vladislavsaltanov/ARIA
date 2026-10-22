export const RECONNECT_MIN_MS = 500;
export const RECONNECT_MAX_MS = 5000;
export const STALE_MS = 2000;

export const nextDelay = (delay) => Math.min(delay * 2, RECONNECT_MAX_MS);

export const isStale = (connected, now, lastMessageAt) => connected && now - lastMessageAt > STALE_MS;

export async function attemptSignIn(pairing, authenticate) {
  if (!pairing) return { state: "login", error: false };
  let result;
  try {
    result = await authenticate(pairing.identifier, pairing.password);
  } catch {
    return { state: "retry" };
  }
  if (result.token) return { state: "ok", token: result.token };
  if (result.denied) return { state: "login", error: true };
  return { state: "retry" };
}
