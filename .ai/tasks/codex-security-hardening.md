# Security hardening do Worker e dependências web

- Agente: Codex
- Branch: `ai/codex/security-hardening`
- Objetivo: reduzir superfícies de DoS/CSRF no Worker público e remover dependências web com vulnerabilidades conhecidas.
- Status: integrado em `dev/proxima-versao` em 02/08/2026.

## Mudanças

- Leitura JSON do Worker passou a ser incremental e limitada por rota antes da desserialização; login também limita o tamanho da senha processada por PBKDF2.
- Login e logout administrativos exigem a origem exata configurada do dashboard, evitando requisições cross-site com o cookie `SameSite=None`.
- Respostas do Worker recebem `Cache-Control: no-store`, `Referrer-Policy: no-referrer` e `X-Content-Type-Options: nosniff`.
- Dependências do site foram atualizadas/forçadas para versões corrigidas de React RSC, Vite, Wrangler, Cloudflare Vite Plugin, PostCSS, Sharp e esbuild. `npm audit` ficou sem achados.

## Validação

- Worker: 123 testes aprovados.
- Dashboard: 43 testes aprovados.
- Site: `npm ci`, lint, build e 3 testes aprovados; `npm audit` com 0 vulnerabilidades.
- .NET: build e testes Release aprovados.
- `scripts/Verify-Safety.ps1`, `dotnet format --verify-no-changes` e `git diff --check` aprovados.
- Auditorias: NuGet e Worker com 0 vulnerabilidades conhecidas.

## Decisões e limitações

- Nenhuma versão, release, instalador ou deploy foi alterado.
- O dashboard não possui lockfile; sua suíte não depende de pacotes externos e foi executada diretamente.

## Commit

- Commit local único desta tarefa: `security: harden worker requests and web dependencies`.
