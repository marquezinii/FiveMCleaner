import { validateBatch } from './validateEvent.js';

// FiveMCleaner anonymous telemetry Worker. Not deployed yet -- see
// wrangler.toml and the module docs/telemetry.md for the data contract this
// enforces. The .NET client does not send to this endpoint yet either; this
// is scaffolding for a future increment (local queue + HTTP batch transport).
export default {
  async fetch(request, env) {
    if (request.method !== 'POST') {
      return new Response('Method not allowed', { status: 405 });
    }

    let payload;
    try {
      payload = await request.json();
    } catch {
      return new Response('Invalid JSON', { status: 400 });
    }

    const events = validateBatch(payload);
    if (events === null) {
      return new Response('Event batch failed validation', { status: 400 });
    }

    const receivedAt = new Date().toISOString();
    const statement = env.TELEMETRY_DB.prepare(
      'INSERT INTO telemetry_events (event_name, execution_time_ms, app_version, error_category, environment, received_at) VALUES (?, ?, ?, ?, ?, ?)',
    );
    const batch = events.map((event) =>
      statement.bind(
        event.eventName,
        event.executionTimeMs,
        event.appVersion,
        event.errorCategory,
        event.environment,
        receivedAt,
      ),
    );
    await env.TELEMETRY_DB.batch(batch);

    return new Response(null, { status: 202 });
  },
};
