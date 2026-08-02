# Cadastro, login e Termos de Uso

- Agente: Codex
- Branch: `ai/codex/account-registration-terms`
- Objetivo: entregar cadastro/login funcional com perfil completo, consentimento versionado e integração D1.
- Status: concluído, implantado e validado no fluxo real.

## Mudanças

- Formulário WPF profissional com nome, sobrenome, usuário, e-mail, senha, repetição de senha, campos obrigatórios e feedback de carregamento/erro.
- Checkbox obrigatório e link azul para uma janela local com Termos de Uso completos.
- Correção do crash de runtime que impedia a janela de conta existente de abrir (`Mica` sem extensão da barra de título).
- Contrato cliente/Worker atualizado com usuário único e aceite dos termos `2026-08-02`.
- D1 registra versão/data do aceite, limita cinco cadastros por IP com HMAC a cada hora e guarda somente o hash SHA-256 do token de sessão.
- Migração incremental para o banco implantado e snapshot completo para bancos novos.

## Validação

- `dotnet test FiveMCleaner.slnx -c Release --no-restore` aprovado.
- Smoke test WPF de abertura das janelas de conta e termos aprovado.
- `dotnet format FiveMCleaner.slnx --verify-no-changes --no-restore` aprovado.
- `scripts\Verify-Safety.ps1` aprovado.
- Worker: 124 testes aprovados, `npm audit` sem vulnerabilidades e `wrangler deploy --dry-run` aprovado.
- Migração e schema novo aplicados e inspecionados em bancos D1 locais isolados.
- Reserva atômica do limite de cadastro validada no D1: cinco tentativas aceitas e a sexta recusada sem ultrapassar o contador.
- Migração `0001_account_username_terms.sql` aplicada no D1 remoto com backup automático do Wrangler.
- Worker implantado na versão `cf21d454-79b4-4ee2-9b44-2fa119863167`.
- Smoke remoto aprovado para cadastro, login, consulta de sessão, logout e rejeição da sessão revogada; conta, sessões e controle antiabuso de teste removidos e confirmados em zero.
- `git diff --check` aprovado.

## Decisões e limites

- O aceite é validado no cliente e novamente no Worker; campos extras enviados pelo cliente nunca substituem a validação do servidor.
- Verificação de e-mail e recuperação de senha dependem de um domínio/remetente e provedor de entrega e não foram inventadas nesta tarefa.
- Nenhum dado de teste deve permanecer no D1 real após o smoke test remoto.
