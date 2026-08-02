// Reads and parses a request body as JSON. Returns the parsed value, or the
// JSON_PARSE_FAILED sentinel when the body is not valid JSON. The caller
// decides what to return on failure: the ingest routes answer with a
// plain-text 400 ('Invalid JSON'), while the login route answers with a JSON
// error body ({error: 'invalid-request'}) -- two different response shapes
// that this helper must not conflate.
//
// A literal JSON `null` body parses successfully and is NOT a parse failure:
// it flows on to the domain validator, which rejects it with the domain's
// own message. Using a sentinel (rather than returning null) keeps that
// distinction intact.

export const JSON_PARSE_FAILED = Symbol('JSON parse failed');

export async function readJsonBody(request) {
  try {
    return await request.json();
  } catch {
    return JSON_PARSE_FAILED;
  }
}
