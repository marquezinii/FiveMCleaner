# firebase-id-token-verify

- **Agente:** opencode
- **Branch:** `ai/opencode/firebase-id-token-verify`
- **Worktree:** `C:\Projetos\FiveMCleaner-ai-opencode-firebase-id-token`
- **Objetivo:** Fechar o pendente de verificação de ID Token Firebase no Worker
- **Status:** pronto para integração

## Resumo

Implementado o verificador de Firebase ID Token no Worker (sem rota de
produto e sem mudança no cliente desktop), conforme escopo aprovado.

## Mudanças principais

- `infra/cloudflare-worker/src/auth/firebaseIdToken.js` — `verifyFirebaseIdToken`
  e `requireFirebaseUser` (RS256, JWKS Google, aud/iss/exp/sub, cache JWKS,
  401 genérico)
- `infra/cloudflare-worker/test/auth/firebaseIdToken.test.js` — 12 testes
- Docs: README do Worker, `docs/architecture.md`, spec em
  `docs/superpowers/specs/2026-08-04-firebase-id-token-verify-design.md`
- Contas legadas: sem migração (nenhum usuário real a preservar); cleanup de
  tabelas `user_*` fica para deploy/migration autorizado futuro

## Testes

- `npm test` em `infra/cloudflare-worker`: **132 pass / 0 fail**

## Fora desta tarefa (manual / futuro)

- Deploy do Worker (só necessário quando houver rota autenticada ou integração
  autorizada a publicar o código)
- Primeira rota de produto autenticada
- Cliente C# anexar `Authorization: Bearer`
- DROP remoto de tabelas legadas `user_*`
- Atualização de `PROJECT_STATE.md` (agente integrador)

## Commits

Ver histórico da branch após o commit desta tarefa.
