# Objective

- **Agente:** Codex
- **Objetivo:** Preserve the enqueue order of persisted telemetry events when multiple events share the same clock tick.
- **Escopo:** Queue filename generation and the existing ordering regression check.
- **Fora do escopo:** Telemetry payloads, transport, collection policy, and worker contracts.
- **Critérios de conclusão:** Pending events are read in enqueue order under rapid sequential writes, and the relevant test is repeatably validated.
- **Resultado entregue:** Monotonic queue filename timestamps prevent GUID ordering from reordering same-tick events.
