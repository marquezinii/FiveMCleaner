# Estrutura do código

## 1) Mapa de topo

| Caminho | Finalidade | Evidência |
| --- | --- | --- |
| `src/` | Projetos .NET do app, domínio, Windows, broker e atualização | `Ralven.slnx`, `docs/architecture.md` |
| `tests/Ralven.Tests/` | Testes .NET por área (`App`, `Core`, `Windows`, `UpdateRuntime`) | `tests/Ralven.Tests/Ralven.Tests.csproj` |
| `infra/cloudflare-worker/` | API Cloudflare Worker, D1, migrations e testes | `infra/cloudflare-worker/wrangler.toml` |
| `infra/dashboard/` | Painel administrativo estático e testes | `infra/dashboard/index.html`, `infra/dashboard/assets/app.js` |
| `installer/` | Instalador Inno Setup e textos localizados | `installer/Ralven.iss` |
| `scripts/` | Build, verificação de segurança, instalador e atalho de desenvolvimento | `scripts/Verify-Safety.ps1`, `scripts/Build-Installer.ps1` |
| `assets/brand/` | Fonte oficial de marca, exports, Inter, tokens e checksums | `assets/brand/guidelines/BRAND_GUIDELINES.md` |
| `docs/` | Arquitetura, segurança, telemetria, release e operação | `docs/architecture.md`, `docs/safety.md` |
| `.github/` | CI, Pages, release e templates de colaboração | `.github/workflows/ci.yml` |
| `build/` | Configuração de obfuscação e build de distribuição | `build/obfuscation/Ralven.Obfuscar.xml` |

`artifacts/`, `bin/`, `obj/`, `.next/` e `out/` são saídas geradas, não fonte.

## 2) Entry points

- Desktop: `src/Ralven.App/App.xaml` e `App.xaml.cs` (`OnStartup`).
- Broker elevado: `src/Ralven.Broker/Program.cs`.
- Launcher, updater e ferramenta de release: `src/Ralven.Launcher/Program.cs`, `src/Ralven.Updater/Program.cs`, `src/Ralven.ReleaseTool/Program.cs`.
- Worker: export default de `infra/cloudflare-worker/src/index.js`.
- Dashboard: `infra/dashboard/index.html` carrega `assets/app.js`.
- Instalador: `installer/Ralven.iss`.

`Ralven.slnx` seleciona os projetos .NET; os `package.json` selecionam scripts Node; os workflows em `.github/workflows/` coordenam CI e publicação.

## 3) Fronteiras de módulos

| Módulo | Pertence aqui | Não pertence aqui |
| --- | --- | --- |
| `Ralven.App` | WPF, navegação, localização, ViewModels e serviços de aplicação | Escrita privilegiada direta no Windows |
| `Ralven.Contracts` | DTOs, IDs, enums, resultados e serialização compartilhada | WPF ou implementações Windows |
| `Ralven.Core` | Catálogo, políticas e construção de planos | UI, registro ou filesystem concreto |
| `Ralven.Windows` | Descoberta, ações e transação Windows e Jogos | Decisão visual ou ampliação de produto |
| `Ralven.Broker` | Execução administrativa tipada e allowlisted | Shell livre, rede, telemetria ou UI |
| `Launcher` / `Updater` / `UpdateRuntime` / `ReleaseTool` | Staging, assinatura, ativação, health-check e rollback | Enfraquecer validação para aceitar pacote inválido |
| `infra/cloudflare-worker` | Validação server-side, auth, D1 e APIs | Confiar em identidade ou validação enviada pelo cliente |

## 4) Organização e nomes

- C#: diretórios por camada e depois por responsabilidade; tipos e arquivos em PascalCase (`PlanBuilder.cs`, `WindowsTransactionEngine.cs`).
- WPF: views em `Views/`, páginas em `Views/Pages/`, recursos em `Themes/` e textos em `Resources/Strings*.resx`.
- Worker: módulos por domínio (`auth/`, `bugReports/`, `liveAlert/`, `stats/`, `updaterEvents/`) e arquivos JavaScript em camelCase.
- Site: componentes React em PascalCase e conteúdo central em `app/content/copy.ts`.
- Imports .NET seguem referências explícitas de projeto; o site configura o alias `@/*`, embora imports relativos também sejam usados.

## 5) Evidências

- `Ralven.slnx`
- `AGENTS.md`
- `docs/architecture.md`
- `src/Ralven.App/App.xaml.cs`
- `src/Ralven.Windows/WindowsOptimizationRuntime.cs`
- `infra/cloudflare-worker/src/index.js`
