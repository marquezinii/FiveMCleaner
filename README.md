<p align="center"><img src="docs/assets/icon.png" alt="FiveMCleaner" width="112"></p>

<h1 align="center">FiveMCleaner</h1>

<p align="center">Diagnóstico, manutenção e otimização transparente para <strong>FiveM sobre GTAV Legacy</strong> no Windows.</p>

<p align="center"><a href="https://marquezinii.github.io/FiveMCleaner/"><strong>Baixar para Windows</strong></a> · <a href="https://github.com/marquezinii/FiveMCleaner/releases">Releases</a> · <a href="docs/safety.md">Segurança</a> · <a href="docs/architecture.md">Arquitetura</a></p>

<p align="center"><a href="https://github.com/marquezinii/FiveMCleaner/actions/workflows/ci.yml"><img alt="CI" src="https://img.shields.io/github/actions/workflow/status/marquezinii/FiveMCleaner/ci.yml?branch=main&style=flat-square&label=CI"></a> <a href="https://github.com/marquezinii/FiveMCleaner/releases/latest"><img alt="Release" src="https://img.shields.io/github/v/release/marquezinii/FiveMCleaner?style=flat-square&color=f97316"></a> <img alt="Windows 10 e 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-2563eb?style=flat-square"> <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512bd4?style=flat-square"></p>

![Visão do FiveMCleaner](docs/assets/hero.png)

> [!IMPORTANT]
> O FiveMCleaner suporta somente **FiveM em GTAV Legacy**. GTAV Enhanced é identificado e bloqueado com segurança até existir um adaptador dedicado, pesquisado e testado.

## Otimização explicável, não "tweak" oculto

O FiveMCleaner começa pelo diagnóstico, monta um plano compreensível e mostra o que poderá ser alterado antes de executar. Cada ação tem escopo, pré-condições, risco, resultado e estratégia de rollback quando aplicável.

- Diagnóstico de FiveM/GTA V, CPU, GPU, RAM, armazenamento, drivers, rede, energia, processos e gargalos prováveis.
- Perfis **Leve**, **Médio** e **Agressivo** formados por ações conhecidas, nunca por scripts genéricos.
- Execução transacional com snapshot, verificação, journal local e recuperação.
- Manutenção somente opt-in de dados permitidos; caches protegidos, entitlements, plugins e autenticação nunca são tratados como lixo.
- Histórico, relatório técnico sanitizado e comparação local antes/depois.

> [!WARNING]
> Nenhum software pode prometer FPS, ping ou ausência de stutter em todo PC ou servidor. O FiveMCleaner não desativa Defender, Firewall, SmartScreen ou UAC; não injeta código; não modifica binários ou memória do jogo; e não usa prioridade Realtime, afinidade fixa ou debloat genérico.

## Experiência atual

| Área | Disponível |
| --- | --- |
| Otimizador | Prévia, perfis, progresso real, cancelamento seguro, resultado por ação e rollback. |
| Conta | Cadastro, login, Google com OAuth 2.0 + PKCE, verificação de e-mail, recuperação, troca de senha/e-mail e exclusão. |
| Privacidade | ID token apenas em memória; refresh token protegido por DPAPI; telemetria limitada por consentimento. |
| Relatos de bug | Envio explícito, campos validados, e-mail e trecho de log opcionais; sem anexos automáticos. |
| Atualizações | Feed assinado, validação de origem/tamanho/SHA-256, staging, ativação atômica, health-check e rollback. |
| Dashboard | Métricas e bugs protegidos por autenticação, filtros defensivos e avisos ao vivo para o app. |

## Segurança por projeto

Uma alteração persistente passa por **descobrir → planejar → validar → aplicar → verificar → registrar**. O app principal não fica elevado: ações administrativas atravessam um broker efêmero, tipado e allowlisted.

Dados como `game-storage`, `nui-storage`, `ipfs`, `CitizenFX.ini`, plugins, `ros_id.dat` e entitlements digitais são protegidos. O produto valida instalação, processos, caminhos e reparse points antes de alterar qualquer coisa.

Leia [a política de segurança](docs/safety.md) e [as evidências técnicas](docs/research.md).

## Instalação e atualização

Baixe apenas pelas [Releases oficiais](https://github.com/marquezinii/FiveMCleaner/releases). Cada release `win-x64` inclui instalador, SHA-256, manifestos e pacotes gerados pelo mesmo workflow.

Após a confirmação da pessoa usuária, a atualização verifica a origem e integridade, preserva a versão anterior e só confirma a candidata após ela sinalizar saúde.

> [!NOTE]
> Os binários ainda não têm assinatura Authenticode pública. SmartScreen pode pedir confirmação por reputação. Confira origem, tag e SHA-256; nunca desative proteções para instalar o aplicativo.

## Privacidade

O diagnóstico é local por padrão. Telemetria e crash reporting são sanitizados, allowlisted e nunca alteram o resultado de uma otimização. O dashboard não recebe arquivos pessoais, credenciais, tokens, cookies, clipboard, paths completos, dumps ou logs completos sem uma ação explícita.

Veja [telemetria](docs/telemetry.md) e [relatos de bug](docs/bug-reports.md).

## Desenvolvimento

Requisitos: Windows 10/11 x64, [.NET SDK 10.0.303](https://dotnet.microsoft.com/download/dotnet/10.0) e Node.js 24.19.0 LTS.

```powershell
dotnet restore FiveMCleaner.slnx
dotnet build FiveMCleaner.slnx --configuration Release --no-restore
dotnet test FiveMCleaner.slnx --configuration Release --no-build
.\scripts\Verify-Safety.ps1
```

Para abrir uma demonstração segura sem FiveM/GTA instalado:

```powershell
.\scripts\Install-DevelopmentShortcut.ps1 -Build
```

Ela usa dados sintéticos e não grava configurações nem executa ações do sistema.

## Arquitetura

```text
App             WPF, conta, configurações e serviços de aplicação
Contracts       DTOs, IDs e contratos duráveis
Core            políticas, catálogo, planos e rollback
Windows         descoberta e integrações de Windows/FiveM
Broker          operações administrativas tipadas e allowlisted
Launcher/Updater atualização transacional e supervisão pós-update
Worker/Dashboard backend de conta, telemetria, bugs e operação privada
Website         site público estático
```

Consulte [docs/architecture.md](docs/architecture.md) para as fronteiras completas.

## Status da v1.4.0

- Avisos ao vivo do dashboard para o aplicativo e notas pós-atualização.
- Telemetria v5 mínima, consentida e acompanhada no painel.
- Interface renovada em Visão geral, Otimizador e Histórico.
- Cadeia de distribuição endurecida com validação fail-closed dos binários distribuídos.

FiveMCleaner é um projeto comunitário independente, sem afiliação, endosso ou patrocínio de Rockstar Games, Cfx.re ou FiveM.
