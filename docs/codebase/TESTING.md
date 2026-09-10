# Padrões de teste

## 1) Stack e comandos

- .NET: Microsoft.Testing.Platform 2.3.3, xUnit v3 3.2.2 e coverlet.MTP 10.0.1.
- Node: runner nativo `node:test` e `node:assert/strict`.
- Site: build Next estático antes de validar o HTML exportado.

```powershell
dotnet run --project tests/Ralven.Tests/Ralven.Tests.csproj --configuration Release --no-build -- --minimum-expected-tests 1
./scripts/Verify-Safety.ps1
```

```powershell
Set-Location infra/cloudflare-worker
npm test
```

```powershell
Set-Location infra/dashboard
npm test
```

```powershell
Set-Location website
npm test
```

A CI seleciona os jobs pela área alterada e mantém um único status obrigatório.
Ela também valida o contrato do repositório (PR/objetivo, versões, catálogos de
localização, placeholders, roadmap, códigos de bug entre .NET/Worker/dashboard
e toolchain Node), executa formatação e
restore com auditoria NuGet, build, lint/typecheck/build do site, `npm audit` e
geração de SBOM. Mudanças no instalador executam build e smoke test próprios;
uma rodada semanal repete as auditorias de dependências mesmo sem mudanças.
O projeto xUnit v3 é executável; o comando usa `dotnet run` e exige ao menos
um teste porque o caminho `dotnet test`/servidor do SDK 10 atualmente retorna
zero testes nesta combinação WPF/MTP.

## 2) Layout

- .NET: um projeto em `tests/Ralven.Tests`, subdividido em `App/`, `Core/`, `Windows/` e `UpdateRuntime/`; arquivos `*Tests.cs`.
- Worker: `infra/cloudflare-worker/test/**/*.test.js`, espelhando domínios de `src/`.
- Dashboard: `infra/dashboard/test/*.test.js`.
- Setup compartilhado .NET: `GlobalUsings.cs`, `SharedTestDoubles.cs` e doubles locais por área.

## 3) Matriz de escopo

| Escopo | Coberto? | Alvo típico | Observação |
| --- | --- | --- | --- |
| Unitário | sim | catálogo, políticas, validators, stores, URLs, sanitização | maioria usa doubles/in-memory |
| Integração local | sim | transação/journal, updater em diretórios temporários, D1 simulado, HTML exportado | não exige FiveM, UAC ou rede real |
| Contrato | sim | JSON/enum persistido, localização, temas, installer, assinatura e assets | evita drift entre camadas e artefatos |
| E2E real | parcial | captura WPF em demo e smoke de instalador por scripts | CI não comprova máquina real/FiveM/UAC/backend publicado |

## 4) Mocking e isolamento

- C#: interfaces em `Ralven.Windows` e serviços App recebem fakes/doubles; filesystem usa diretórios temporários e journals in-memory quando possível.
- Worker: `Request`, `fetch` injetado, bindings e D1 simulados em JavaScript puro.
- Dashboard: helpers sem DOM são testados diretamente.
- Testes unitários não devem depender da instalação real do FiveM, estado da máquina, elevação ou rede externa.

Falha comum a evitar: alterar contrato persistido, resource key ou metadata de ação sem atualizar o teste de contrato correspondente.

## 5) Cobertura e sinais de qualidade

- coverlet gera Cobertura na CI; não há threshold mínimo configurado.
- Cobertura atual: `[TODO]` nenhum percentual versionado no repositório.
- A CI cancela execução anterior da mesma ref e guarda resultados por 14 dias.
- Jobs não afetados são ignorados; execução manual roda a matriz completa e a
  agenda semanal cobre .NET, Worker, dashboard e site sem gerar SBOM/instalador.
- Gaps conhecidos: Authenticode real, comportamento em hardware/FiveM reais, fluxo UAC instalado e disponibilidade dos serviços publicados precisam de validação fora da suíte unitária.
- Não há benchmark ou suíte de performance configurada.

## 6) Evidências

- `tests/Ralven.Tests/Ralven.Tests.csproj`
- `tests/Ralven.Tests/Windows/WindowsTransactionEngineTests.cs`
- `tests/Ralven.Tests/App/SignedManifestUpdateServiceDownloadTests.cs`
- `infra/cloudflare-worker/test/telemetryIngest.test.js`
- `infra/dashboard/test/api.test.js`
- `.github/workflows/ci.yml`
