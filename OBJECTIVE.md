# Objective

- **Agente:** Codex
- **Objetivo:** Integrate the Worker dependency update into `dev/proxima-versao` without changing the protected `main` branch.
- **Escopo:** Update the Worker lockfile and its direct Wrangler development dependency while preserving the current Worker configuration.
- **Fora do escopo:** Worker behavior, migrations, application code, and release publication.
- **Critérios de conclusão:** `wrangler` and its resolved dependency tree are updated in the Worker manifest and lockfile, and Worker tests pass.
- **Resultado entregue:** Updated Wrangler and the resolved Worker lockfile while preserving the existing D1 configuration; Worker tests were run after installing the locked dependencies.
