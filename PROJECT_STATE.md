# Estado do Projeto

## Visão geral e objetivo

FiveMCleaner é um aplicativo desktop para Windows voltado à otimização
transparente, reversível e orientada por diagnóstico do FiveM para GTAV Legacy.
Ele prioriza mudanças pequenas, verificáveis e com rollback, sem prometer ganho
universal de FPS nem comprometer proteções do sistema.

## Tecnologias

- C# / .NET SDK 10.0.302, definido em `global.json`;
- WPF para a aplicação desktop (`net10.0-windows10.0.19041.0`);
- xUnit para testes automatizados;
- PowerShell para automação de validação, pacote portável e instalador;
- Inno Setup para o instalador Windows self-contained;
- Next.js, React, TypeScript, Vite/Vinext e ESLint para o site em `website/`;
- GitHub Actions para CI e para o workflow manual de release.

## Arquitetura

A solução `FiveMCleaner.slnx` separa responsabilidades em projetos:

- `FiveMCleaner.App`: interface WPF, tema, localização, diagnósticos exibidos,
  progresso, preferências e interação do usuário; não deve executar operações
  administrativas diretamente.
- `FiveMCleaner.Contracts`: DTOs, identificadores, enums e contratos tipados
  compartilhados entre processos.
- `FiveMCleaner.Core`: catálogo de ações, composição dos perfis e planejamento
  de otimização; não depende de WPF, registro ou sistema de arquivos.
- `FiveMCleaner.Windows`: descoberta de FiveM/GTA e adaptadores Windows para
  ações permitidas, filesystem, registro e transações.
- `FiveMCleaner.Broker`: processo administrativo efêmero, com allowlist e
  contratos validados; não aceita shell, scripts nem comandos arbitrários.
- `FiveMCleaner.Tests`: testes de contratos, planejamento, ações Windows,
  rollback e serviços da aplicação.

O fluxo central é: diagnóstico factual, criação de plano imutável, prévia e
consentimento, snapshot, execução por ação, validação, journal local e rollback
quando aplicável. O broker recebe apenas o subconjunto administrativo já
aprovado e tipado.

## Estrutura relevante

```text
src/                         Aplicação e camadas .NET
tests/FiveMCleaner.Tests/    Testes xUnit
scripts/                     Validações, pacote e instalador
installer/                   Script Inno Setup e contrato de release
docs/                        Arquitetura, segurança, pesquisa e distribuição
.github/workflows/           CI e release manual
website/                     Landing page Next/React independente
artifacts/, publish/, tmp/   Saídas locais ignoradas pelo Git
```

## Decisões técnicas e padrões

- O produto atende apenas FiveM para GTAV Legacy. GTAV Enhanced deve ser
  detectado e bloqueado com segurança até existir um adaptador específico.
- Cada ação de sistema precisa ter escopo conhecido, pré-condições,
  pós-validação, resultado tipado e estratégia de rollback quando possível.
- Perfis Leve, Médio e Agressivo são composições de ações versionadas; eles não
  executam operações diretamente.
- Caches e arquivos sensíveis são tratados por allowlist e condições
  explícitas. Dados de autenticação, `game-storage`, NUI storage,
  configurações e plugins não são lixo automático.
- A interface é localizada para pt-BR e inglês, com tema claro, escuro ou do
  sistema. Identificadores de código permanecem em inglês.
- Preferências, journals, solicitações efêmeras e logs locais ficam sob
  `%LOCALAPPDATA%\FiveMCleaner`; não devem ser gravados dentro da pasta de
  instalação.
- O instalador publica o runtime .NET junto ao aplicativo (`win-x64`
  self-contained). O atualizador consulta apenas releases estáveis públicas do
  GitHub, valida versão, origem HTTPS e SHA-256, e só abre o instalador após
  confirmação explícita do usuário.
- O formulário de bugs é opt-in: nenhum dado é enviado sem o clique do usuário.
  Imagens opcionais passam por sanitização antes do envio.

## Funcionalidades presentes no código atual

- Diagnóstico de FiveM Legacy, GTA, CPU, GPU, memória, armazenamento, cache e
  processos relevantes.
- Modos de otimização Leve, Médio e Agressivo, com planejamento e progresso por
  etapas.
- Ações reversíveis e restritas para configurações gráficas Legacy, Game Mode,
  preferências de GPU, energia de sessão, captura em segundo plano, efeitos
  visuais e limpezas condicionadas.
- Journal transacional, snapshots e rollback por ação.
- Broker elevado de escopo mínimo, atualização opcional por GitHub Releases,
  atalho de desenvolvimento, ícone oficial, bandeja, inicialização opcional,
  suporte de idioma/tema e formulário de bugs.
- Instalador Inno Setup, pacote portável self-contained, manifestos e checksums
  de release.
- Landing page e documentação de segurança, instalação, pesquisa, bugs e
  streaming.

## Limitações e cuidados conhecidos

- Não há promessa de FPS ou de ausência de falso positivo em todos os
  antivírus. A versão sem assinatura Authenticode pode receber avisos de
  reputação/SmartScreen.
- O produto não desativa Defender, firewall, SmartScreen, UAC, Windows Update
  ou serviços essenciais; não cria exclusões de antivírus, não injeta código,
  não altera memória nem baixa/executa código arbitrário.
- Não há suporte operacional para GTAV Enhanced.
- Testes que alterariam uma instalação real de Windows/FiveM são opt-in; a
  suíte padrão usa doubles e diretórios temporários.
- O build, lint e testes renderizados do site passam, mas `npx tsc --noEmit`
  atualmente reporta tipos ausentes do runtime Cloudflare (`cloudflare:workers`,
  `Fetcher` e `D1Database`). Isso deve ser tratado como uma limitação conhecida
  do site, não mascarado por uma cadeia de comandos.
- O conteúdo de `website/` faz parte do repositório principal. Seus artefatos
  gerados e credenciais locais são ignorados tanto pela raiz quanto pelo
  `.gitignore` específico do site.

## Comandos de desenvolvimento e validação

Na raiz do repositório:

```powershell
dotnet restore FiveMCleaner.slnx
dotnet build FiveMCleaner.slnx --configuration Release --no-restore
dotnet test FiveMCleaner.slnx --configuration Release --no-build
.\scripts\Verify-Safety.ps1
dotnet run --project src\FiveMCleaner.App\FiveMCleaner.App.csproj
.\scripts\Build-Portable.ps1
.\scripts\Build-Installer.ps1 -Version <versão>
```

Para o site:

```powershell
Set-Location website
npm ci
npm run build
npm run lint
npm test
npx tsc --noEmit
```

O `npm test` do site já executa o build antes dos testes de HTML renderizado.
Use `docs/installer.md`, `docs/safety.md` e `docs/architecture.md` como contexto
complementar, mas confirme sempre o comportamento no código e nos testes.
