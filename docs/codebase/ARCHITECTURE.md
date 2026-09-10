# Arquitetura do código

## 1) Estilo arquitetural

- Estilo principal: aplicação desktop em camadas, com adaptadores Windows e processos auxiliares; backend e sites são deployables separados no mesmo repositório.
- Classificação: `Contracts` e `Core` não dependem de WPF; `Windows` implementa efeitos concretos; `App` orquestra UI; o broker isola privilégio. As referências dos `.csproj` confirmam essas direções.
- Restrições centrais: a integração FiveM atende somente GTAV Legacy; descobrir antes de alterar; operações persistentes verificáveis e reversíveis; privilégio mínimo; contratos remotos allowlisted.

Não há contêiner de DI. A composição é explícita em factories como `WindowsOptimizationDependencies.CreateDefault` e no startup da janela, o que também simplifica doubles nos testes.

## 2) Fluxo principal de otimização

```text
WPF/ViewModel -> AppOptimizationService -> PlanBuilder/Core -> Windows runtime
             -> transaction journal -> broker tipado quando necessário -> resultado/rollback -> UI
```

1. `App.OnStartup` aplica políticas globais, garante instância única, importa dados locais permitidos e abre `MainWindow`.
2. `MainViewModel` chama `IAppOptimizationService` para diagnóstico, prévia e execução.
3. `AppOptimizationService` detecta fatos de Windows/FiveM e envia uma requisição tipada ao `PlanBuilder`.
4. `ActionCatalog` e `WindowsActionCatalog` validam ID, versão e metadata antes de resolver handlers concretos.
5. `WindowsTransactionEngine` aplica, verifica, registra snapshot/journal e faz rollback segundo o outcome; ações administrativas são adiadas para o broker.
6. `ElevatedBrokerClient` entrega um plano limitado ao `Ralven.Broker`; o broker revalida o request, executa com timeout e devolve eventos por named pipe.

## 3) Responsabilidades

| Camada/módulo | Possui | Não deve possuir | Evidência |
| --- | --- | --- | --- |
| App | UI, ViewModels, consentimento, configuração e orquestração | Mutação Windows privilegiada direta | `src/Ralven.App/`, `MainWindow.Startup.xaml.cs` |
| Contracts | Protocolo estável, estados e JSON | Implementação de plataforma | `src/Ralven.Contracts/RalvenJson.cs` |
| Core | Catálogo e planejamento | WPF e I/O de Windows | `src/Ralven.Core/Planning/PlanBuilder.cs` |
| Windows | Descoberta, ações, adaptadores e transação | Escolha de produto pelo usuário | `src/Ralven.Windows/WindowsOptimizationRuntime.cs` |
| Broker | Fronteira elevada allowlisted | Comando arbitrário, rede ou navegação | `src/Ralven.Broker/Program.cs`, `PlanValidator.cs` |
| Update runtime | Integridade, staging, ativação e recuperação | Ignorar assinatura/hash/origem | `src/Ralven.UpdateRuntime/` |
| Worker | Auth, validação, rate limiting, D1 e respostas | Confiar no cliente para identidade | `infra/cloudflare-worker/src/index.js` |
| Dashboard/site | Operação privada / comunicação pública | Lógica de mutação local | `infra/dashboard/`, site institucional Vemryx |

## 4) Padrões reutilizados

| Padrão | Onde | Razão |
| --- | --- | --- |
| Catálogo/Strategy de ações | `Core/Catalog`, `Windows/Actions` | Só IDs e handlers conhecidos entram em planos |
| Adapter + composição explícita | `WindowsOptimizationDependencies` | Produção usa Windows real; testes injetam doubles |
| Transação com journal e snapshot | `WindowsTransactionEngine`, `TransactionJournal` | Verificação, retomada e rollback sem apagar outcomes |
| Broker de privilégio mínimo | `ElevatedBrokerClient`, `Ralven.Broker` | App principal permanece sem elevação permanente |
| Fail-closed | endpoint de telemetria, Firebase JWT, assinatura de update | Configuração ou autenticação inválida não amplia confiança |
| Outbox local limitada | `LocalTelemetryQueue` | Falha de rede não afeta otimização e retry preserva idempotência |
| Sanitização/allowlist | telemetria, crash, bug report e Worker | Impede texto livre e dados sensíveis fora do contrato |

## 5) Startup e fluxos remotos

- Consentimento é resolvido antes de Sentry e telemetria; demo não persiste nem transmite.
- App → Worker usa HTTPS com redirects desativados e timeouts; perfis usam Firebase Bearer validado no Worker.
- Worker valida rota/payload/rate limit, executa queries D1 e retorna erros estruturados; dashboard usa cookie administrativo e CSRF.
- Updater valida manifestos/artefatos antes de staging e só confirma ativação após recibo de saúde.

## 6) Riscos arquiteturais conhecidos

- `WindowsTransactionEngine`, updater e broker são fronteiras de segurança: uma simplificação de outcomes, assinatura ou rollback pode causar perda de dados ou elevação indevida.
- `AppOptimizationService` concentra diagnóstico, settings e orquestração; mudanças amplas nele têm grande superfície de regressão.
- Worker de Development e Production compartilha o mesmo D1 e separa dados por coluna `environment`; a separação é lógica, não física.

## 7) Evidências

- `docs/architecture.md`
- `docs/safety.md`
- `src/Ralven.App/App.xaml.cs`
- `src/Ralven.App/Services/AppOptimizationService.cs`
- `src/Ralven.Core/Planning/PlanBuilder.cs`
- `src/Ralven.Windows/Engine/WindowsTransactionEngine.cs`
- `src/Ralven.Broker/Program.cs`
- `infra/cloudflare-worker/src/index.js`
