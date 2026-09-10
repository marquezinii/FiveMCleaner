# Stack de tecnologia

## 1) Resumo de runtimes

| Área | Linguagem/runtime | Gerenciador e build | Evidência |
| --- | --- | --- | --- |
| Aplicativo Windows | C# 14, .NET SDK 10.0.303, `net10.0-windows10.0.19041.0` | NuGet com versões centralizadas; MSBuild/Solution XML | `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `Ralven.slnx` |
| Worker, dashboard e site | JavaScript/TypeScript, Node.js 24.19.0 | npm com lockfiles; Cloudflare Workers/Pages e export estático Next | `.node-version` e os três `package.json` |

O desktop é a aplicação principal; Worker, dashboard e site são superfícies implantáveis separadamente.

## 2) Dependências de produção principais

| Dependência | Versão | Papel | Evidência |
| --- | --- | --- | --- |
| WPF / Windows Desktop | .NET 10 | Shell nativo, XAML, bandeja e integração Windows | `src/Ralven.App/Ralven.App.csproj` |
| WPF-UI | 4.3.0 | Controles e ícones WPF | `Directory.Packages.props`, `src/Ralven.App/Ralven.App.csproj` |
| Sentry | 6.8.0 | Crash reporting condicionado ao controle de relatórios opcionais | `Directory.Packages.props`, `src/Ralven.App/Services/SentryCrashReportingService.cs` |
| `System.Management` / `PerformanceCounter` | 10.0.11 | Descoberta de hardware e métricas Windows | `Directory.Packages.props`, `src/Ralven.Windows/Ralven.Windows.csproj` |
| `ProtectedData` | 10.0.11 | DPAPI para sessão e estado sensível local | `Directory.Packages.props`, `src/Ralven.UpdateRuntime/Ralven.UpdateRuntime.csproj` |
| Cloudflare Workers + D1 | plataforma | API, autenticação administrativa, telemetria, bugs e manifestos | `infra/cloudflare-worker/wrangler.toml`, `infra/cloudflare-worker/src/index.js` |

O Worker e o dashboard não têm dependências npm de produção; usam APIs nativas
da plataforma e do navegador. O site usa Next e React nas versões fixadas em
`website/package.json`.

## 3) Ferramentas de desenvolvimento

| Ferramenta | Uso | Evidência |
| --- | --- | --- |
| Microsoft.Testing.Platform + xUnit v3 | Suíte .NET | `global.json`, `tests/Ralven.Tests/Ralven.Tests.csproj` |
| coverlet.MTP | Cobertura .NET na CI | `Directory.Packages.props`, `.github/workflows/ci.yml` |
| `node:test` | Testes do Worker e dashboard | manifests em `infra/` |
| Wrangler 4.129 | Desenvolvimento, migrations e deploy do Worker | `infra/cloudflare-worker/package.json` |
| Obfuscar / SBOM Tool | Hardening e SBOM dos artefatos | `.config/dotnet-tools.json`, `.github/workflows/ci.yml` |
| PowerShell / Inno Setup | Build, verificações e instalador | `scripts/`, `installer/Ralven.iss` |

## 4) Comandos principais

```powershell
dotnet restore Ralven.slnx
dotnet build Ralven.slnx --configuration Release --no-restore
dotnet run --project tests/Ralven.Tests/Ralven.Tests.csproj --configuration Release --no-build -- --minimum-expected-tests 1
./scripts/Verify-Safety.ps1
```

```powershell
Set-Location infra/cloudflare-worker
npm ci
npm test
```

```powershell
Set-Location infra/dashboard
npm ci
npm test
```

## 5) Ambiente e configuração

- Configuração do app: `src/Ralven.App/Config/appsettings*.json`, com overlay local ignorado `appsettings.<Environment>.local.json`.
- Ambiente do app: `RALVEN_ENVIRONMENT`, argumentos de demonstração e configuração `Development`/`Production` em `src/Ralven.App/Services/AppEnvironment.cs`.
- Worker: bindings `TELEMETRY_DB` e rate limiters no `wrangler.toml`; secrets administrativos são injetados pelo Wrangler e não ficam no Git.
- Restrições: desktop Windows 10/11 x64; Node 24.19.0; não há configuração de container.

## 6) Evidências

- `global.json`
- `Directory.Build.props`
- `Directory.Packages.props`
- `Ralven.slnx`
- `.github/workflows/ci.yml`
- `infra/cloudflare-worker/package.json`
