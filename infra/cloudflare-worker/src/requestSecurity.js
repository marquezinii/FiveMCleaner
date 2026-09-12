/** Parses a JSON Request or Response without buffering more than `maximumBytes`. */
export async function readBoundedJson(message, maximumBytes) {
  const declaredLength = Number(message.headers.get('Content-Length'));
  if ((Number.isFinite(declaredLength) && declaredLength > maximumBytes) || !message.body) {
    return null;
  }

  const reader = message.body.getReader();
  const decoder = new TextDecoder('utf-8', { fatal: true });
  let bytesRead = 0;
  let json = '';
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      bytesRead += value.byteLength;
      if (bytesRead > maximumBytes) {
        await reader.cancel();
        return null;
      }
      json += decoder.decode(value, { stream: true });
    }
    json += decoder.decode();
    return JSON.parse(json);
  } catch {
    // Parser errors can contain fragments of credentials or provider PII.
    console.error('readBoundedJson failed: invalid or unreadable JSON');
    return null;
  }
}

/** Admin JSON endpoints intentionally accept only the exact media type. */
export function hasExactJsonContentType(request) {
  return request.headers.get('Content-Type') === 'application/json';
}
