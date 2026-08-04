/** Parses a JSON request without ever buffering more than `maximumBytes`. */
export async function readBoundedJson(request, maximumBytes) {
  const declaredLength = Number(request.headers.get('Content-Length'));
  if ((Number.isFinite(declaredLength) && declaredLength > maximumBytes) || !request.body) {
    return null;
  }

  const reader = request.body.getReader();
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
  } catch (err) {
    console.error('readBoundedJson failed:', err?.message || 'unknown');
    return null;
  }
}
