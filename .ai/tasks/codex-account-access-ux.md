# Conta: acesso minimalista

- Agente: Codex
- Branch: `ai/codex/account-access-ux`
- Objetivo: tornar o acesso de visitante no topo mais discreto e claro.
- Status: pronto para integração.

## Mudanças

- Removida a aparência de botão em caixa do acesso à conta.
- Substituído o avatar laranja de visitante por ícone neutro escuro.
- Ajustado o rótulo para `Entrar / cadastrar-se`, mantendo o fluxo de login e cadastro existente.

## Validação

- `dotnet test FiveMCleaner.slnx -c Release --no-restore` aprovado.
- `dotnet format FiveMCleaner.slnx --verify-no-changes --no-restore` aprovado.
- `scripts\Verify-Safety.ps1` aprovado.
- `git diff --check` aprovado.

## Observações de integração

- Atualizar o atalho `FiveMCleaner - Desenvolvimento` após integrar em `dev/proxima-versao`, conforme `AI_RULES.md`.
