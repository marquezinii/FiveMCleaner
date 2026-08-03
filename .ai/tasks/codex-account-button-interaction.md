# Interação do botão de conta

- Agente: Codex
- Branch: `ai/codex/account-button-interaction`
- Objetivo: tornar o acesso de conta clicável na barra de título e mostrar a superfície discreta no hover.
- Status: concluído e pronto para integração.

## Diagnóstico

- O botão era um elemento irmão sobreposto à área estendida da barra de título. O `TitleBar` do Wpf.Ui assume o hit-test nativo dessa região, por isso o clique físico não chegava ao controle.

## Mudanças

- Botão movido para `TitleBar.TrailingContent`, a região interativa oficial do componente, com área de clique maior.
- Superfície arredondada transparente em repouso, visível com opacidade no hover/press e foco de teclado perceptível.
- Regressão WPF cobre a abertura da janela pelo evento `Click` do botão.

## Validação

- Teste WPF cobre a abertura da janela de conta pelo evento `Click` do botão.
- `dotnet test FiveMCleaner.slnx -c Release --no-restore` aprovado.
- `dotnet format FiveMCleaner.slnx --verify-no-changes --no-restore` aprovado.
- `scripts\Verify-Safety.ps1` e `git diff --check` aprovados.
