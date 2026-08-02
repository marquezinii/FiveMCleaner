# Telemetria: diagnósticos essenciais e dados opcionais

- Agente: Codex
- Branch: `ai/codex/telemetry-consent`
- Status: pronta para integração

## Alterações

- Elevada a política para consentimento v4, reabrindo a janela somente nesta mudança material.
- A tela informa diagnósticos essenciais sem controle de desativação e mantém um único seletor para dados opcionais.
- Resultados técnicos de otimizações, updater e crash reporting continuam ativos; CPU, GPU, RAM, perfil e ações são omitidos quando o usuário desativa os dados opcionais.
- Removido o controle de crash reports das Configurações e atualizada a documentação de transparência.

## Validação

- `dotnet restore FiveMCleaner.slnx`
- `dotnet test FiveMCleaner.slnx -c Release --no-restore`
- `dotnet format FiveMCleaner.slnx --verify-no-changes --no-restore`
- `scripts\Verify-Safety.ps1`
- `git diff --check`
