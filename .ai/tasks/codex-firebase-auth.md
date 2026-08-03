# Firebase Authentication REST

- Agente: Codex
- Branch: `ai/codex/firebase-auth`
- Status: concluída, pronta para integração

## Mudanças

- Substituído o fluxo de conta próprio do cliente por `FirebaseAuthService`, usando somente a API REST oficial do Firebase.
- Separados DTOs, estado de autenticação, mapeamento de erros e armazenamento DPAPI de sessão. Somente refresh token opcional é persistido; ID token fica em memória e é renovado antes de vencer.
- Implementados cadastro, login, verificação/reenvio, recuperação, logout, reautenticação, alteração de senha/e-mail e exclusão. Fluxos sensíveis não revelam a existência do e-mail.
- A UI bloqueia recursos autenticados até `emailVerified`; o Firebase UID é o identificador interno exposto pelo serviço.
- Removido do Worker o antigo provedor de contas, rotas e schema local. Sem deploy nesta tarefa, os endpoints remotos legados continuam até uma publicação autorizada.

## Validação

- `dotnet test FiveMCleaner.slnx --configuration Release --no-restore` aprovado.
- `npm.cmd test` em `infra/cloudflare-worker` aprovado.
- `dotnet format FiveMCleaner.slnx --verify-no-changes --no-restore` aprovado.
- `scripts\Verify-Safety.ps1` e `git diff --check` aprovados.

## Integração

- Commit local: `feat(auth): integrate Firebase Authentication REST`.
- Nenhum push, deploy ou release foi feito.
- Pendência registrada no `PROJECT_STATE.md`: validar ID Tokens Firebase no
  Worker antes da primeira rota autenticada e decidir a migração/recriação de
  contas antigas antes da primeira release pública que promova o novo fluxo.
