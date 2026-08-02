# Cadastro e login de usuário

- Agente: Codex
- Branch: `ai/codex/user-accounts`
- Status: pronto para integração

## Alterações

- Adicionado botão de perfil no canto superior direito e janela de entrar/criar conta com nome, sobrenome, e-mail, senha e confirmação.
- Criado backend Worker/D1 para cadastro, login, restauração e encerramento de sessão; senhas usam PBKDF2, sessões são opacas/revogáveis e tentativas de login têm limite por IP com hash.
- A sessão local é protegida por DPAPI do Windows e o endpoint de produção é restrito ao host Worker autorizado.

## Validação

- `dotnet test FiveMCleaner.slnx -c Release --no-restore` aprovado.
- `dotnet format FiveMCleaner.slnx --verify-no-changes --no-restore` aprovado.
- `scripts\Verify-Safety.ps1` aprovado.
- `infra\cloudflare-worker`: `npm.cmd test` aprovado (119 testes).
- `git diff --check` aprovado.
- Schema aplicado no D1 remoto e tabelas `user_accounts`, `user_sessions` e `user_login_attempts` confirmadas.
- Worker publicado com sucesso: versão Cloudflare `f7d661dd-f41b-4f76-b5e3-90062088a070`.
- Smoke remoto: sessão sem token `401`, cadastro vazio `400` e manifesto existente `200`.

## Observação de integração

- Backend e schema já estão ativos; a branch do aplicativo ainda precisa ser integrada para disponibilizar a interface na próxima versão.
