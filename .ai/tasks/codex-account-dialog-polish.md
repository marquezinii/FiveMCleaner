# Refinamento da janela de conta

- Agente: Codex
- Branch: `ai/codex/account-dialog-polish`
- Objetivo: tornar o cadastro mais confortável e corrigir fechamento, tipografia e checkbox dos Termos.
- Status: concluído e pronto para integração autorizada.

## Mudanças

- Botão X compacto e acessível para fechar a janela de conta.
- Janela mais larga e campos Nome/Sobrenome dispostos lado a lado.
- Checkbox de Termos redesenhado com marca de seleção estável e menor.
- Rótulo superior ajustado para `Entrar / Cadastre-se` em tipografia variável do Windows, com tamanho menor.

## Validação

- Smoke WPF confirma a largura da janela, o botão X de cancelamento e o checkbox compacto.
- `dotnet test FiveMCleaner.slnx -c Release --no-restore` aprovado.
- `dotnet format FiveMCleaner.slnx --verify-no-changes --no-restore` aprovado.
- `scripts\Verify-Safety.ps1` e `git diff --check` aprovados.
