# Silent Failure Audit

**Agente:** opencode
**Branch:** `ai/opencode/silent-failure-audit`
**Objetivo:** Auditoria de falhas silenciosas em todo o codebase
**Status:** concluido

---

## Resumo executivo

Auditoria cobriu `src/**/*.cs`, `tests/**/*.cs`, `infra/cloudflare-worker/src/**/*.js` e `infra/dashboard/assets/**/*.js`. Encontrados **33 achados**: 2 HIGH, 16 MEDIUM, 14 LOW, 1 NONE.

---

## HIGH

### H1: FirebaseAuthService: LogoutAsync fire-and-forget deixa token stale em disco

**Arquivo:** `src/FiveMCleaner.App/Services/FirebaseAuthService.cs:195`

```csharp
if (error is "INVALID_ID_TOKEN" or "TOKEN_EXPIRED" or "INVALID_REFRESH_TOKEN" or "USER_DISABLED") _ = LogoutAsync();
```

`LogoutAsync` e descartado com `_ =`. Se `sessionStore.ClearAsync()` falhar (IOException por lock de AV, disco cheio), o arquivo de sessao em disco mantem o token stale. Na proxima abertura, o app le o token invalido e pode entrar em loop de login/logout.

**Severidade:** HIGH -- Dados de sessao corrompidos em disco, sem feedback ao usuario.

---

### H2: StreamingSoftwareDetector: catch filter usa `new Exception()` em vez da excecao real

**Arquivo:** `src/FiveMCleaner.App/Services/StreamingSoftwareDetector.cs:264`

```csharp
catch (Exception) when (IsExpectedProbeException(new Exception()))
{
    complete = false;
}
```

O filtro `IsExpectedProbeException(new Exception())` cria uma `new Exception()` **do zero** em vez de usar a excecao capturada. `new Exception()` nunca e `UnauthorizedAccessException`, `SecurityException`, ou `Win32Exception`. Resultado: **o catch nunca executa**. Excecoes de `Task.WaitAll` nas tasks de scan de registro propagam como `AggregateException` nao tratada pelo `Detect()`, que e chamado via `Task.Run`. Pode causar falha silenciosa no fluxo de diagnostico de hardware.

**Severidade:** HIGH -- Bug de logica. Catch morto. Excecoes propagam sem tratamento.

---

## MEDIUM

### M1: CommandRunner: ObserveAsync engole todas as excecoes nao-fatais

**Arquivo:** `src/FiveMCleaner.Windows/Infrastructure/CommandRunner.cs:118-128`

```csharp
private static async Task ObserveAsync(params Task[] tasks)
{
    try { await Task.WhenAll(tasks).ConfigureAwait(false); }
    catch (Exception exception) when (exception is not (
        OutOfMemoryException or StackOverflowException or AccessViolationException)) { }
}
```

Engole toda excecao das tasks de leitura stdout/stderr ao matar processo. Se uma task falhar por problema de encoding ou buffer (nao relacionado ao kill), a falha e invisivel. Sem log, sem check de condicoes especificas.

**Severidade:** MEDIUM -- Falhas genuinas de IO mascaradas como "processo ja morto".

---

### M2: TransactionJournal: Delete() engole IOException ao remover journal

**Arquivo:** `src/FiveMCleaner.Windows/Engine/TransactionJournal.cs:253-256`

```csharp
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
```

Falha ao deletar journal apos transacao completada. Se o journal persiste com estado valido stale, poderia teoricamente disparar rollback duplicado na proxima leitura.

**Severidade:** MEDIUM -- Journal stale pode causar re-aplicacao/rollback incorreto.

---

### M3: UpdaterDiagnostics: RecordAsync perde eventos de telemetria silenciosamente

**Arquivo:** `src/FiveMCleaner.UpdateRuntime/UpdaterDiagnostics.cs:47,61,67,150,156`

Cinco pontos onde `catch (IOException or UnauthorizedAccessException) { }` descarta falhas de escrita/leitura do arquivo de log e da fila de telemetria pendente. O usuario nunca sabe que diagnosticos nao estao sendo persistidos. Cenario de recovery perde audit trail.

**Severidade:** MEDIUM -- Perda silenciosa de telemetria e diagnosticos de atualizacao.

---

### M4: MainViewModel: TrackOptimizationTelemetryAsync usa catch bare

**Arquivo:** `src/FiveMCleaner.App/ViewModels/MainViewModel.cs:1751-1754`

```csharp
catch
{
    // Telemetria e opcional e nao pode afetar a experiencia...
}
```

`catch` sem filtro de excecao pega `OutOfMemoryException`, `StackOverflowException`, `AccessViolationException`. Em .NET moderno, capturar essas pode corromper estado do processo.

**Severidade:** MEDIUM -- Captura de excecoes fatais que deveriam derrubar o processo.

---

### M5: MainViewModel: CheckForUpdatesAsync e fire-and-forget sem tratamento

**Arquivo:** `src/FiveMCleaner.App/ViewModels/MainViewModel.cs:688`

```csharp
_ = CheckForUpdatesAsync();
```

Task descartada. Se `CheckForUpdatesAsync` lancar excecao nao tratada, torna-se `UnobservedTaskException`. Usuario nunca sabe que o check de atualizacao falhou.

**Severidade:** MEDIUM -- Falha de update check invisivel. Potencial crash por unobserved exception.

---

### M6: MainViewModel: SaveSettingsRevisionAsync fire-and-forget

**Arquivo:** `src/FiveMCleaner.App/ViewModels/MainViewModel.cs:1684`

```csharp
_ = SaveSettingsRevisionAsync(BuildSettingsSnapshot(), revision);
```

Se o save falhar (disco cheio, permissao), as configuracoes do usuario sao perdidas silenciosamente. Na proxima inicializacao, preferencias como `privacyConsentVersion`, `selectedLanguage`, `selectedProfile` voltam ao default.

**Severidade:** MEDIUM -- Perda silenciosa de preferencias do usuario.

---

### M7: ResourceUsageInspector: .Result em metodo sincrono sem try-catch

**Arquivo:** `src/FiveMCleaner.Windows/Infrastructure/ResourceUsageInspector.cs:40`

```csharp
return new ResourceUsageSnapshot(cpuTask.Result, diskTask.Result, gpuTask.Result, networkTask.Result);
```

Quatro `.Result` sequenciais. Se `cpuTask.Result` lancar `AggregateException`, as outras 3 tasks nunca tem seus resultados lidos e os `PerformanceCounter` internos podem vazar ate finalizacao. O caller `LiveSystemMetricsProvider.Capture` nao tem try/catch proprio.

**Severidade:** MEDIUM -- Resource leak + falha de metrica ao vivo invisivel.

---

### M8: CloudflareTelemetryService: FlushPendingAsync fire-and-forget

**Arquivo:** `src/FiveMCleaner.App/Services/CloudflareTelemetryService.cs:408`

```csharp
_ = FlushPendingAsync(cancellationToken);
```

Task descartada sem observacao. Embora `FlushPendingAsync` tenha try/catch interno, o cancellation token e perdido e uma excecao inesperada viraria unobserved.

**Severidade:** MEDIUM -- Perda de propagacao de cancelamento + risco de unobserved exception.

---

### M9: Worker index.js: Telemetry action links sem transacao atomica com event rows

**Arquivo:** `infra/cloudflare-worker/src/index.js:207-230`

```javascript
// Primeiro batch: insere event rows
for (const chunk of chunkStatements(statements)) {
    results.push(...await env.TELEMETRY_DB.batch(chunk));
}
// Segundo batch: insere action links
for (const chunk of chunkStatements(actionStatements)) {
    await env.TELEMETRY_DB.batch(chunk);
}
```

D1 nao suporta transacoes multi-statement no Workers. Se o primeiro batch (event rows) sucede e o segundo (action links) falha, as action links sao perdidas permanentemente enquanto os eventos persistem. Cliente recebe 500 e pode reenviar, criando eventos duplicados.

**Severidade:** MEDIUM -- Perda de dados de action links sob falha transiente do D1.

---

### M10: Dashboard app.js: Logout fetch pode falhar silenciosamente

**Arquivo:** `infra/dashboard/assets/app.js:105-107`

```javascript
logoutButton.addEventListener('click', async () => {
    await fetch(`${API_BASE}/admin/logout`, { method: 'POST', credentials: 'include' });
    showLogin(); // roda mesmo se fetch falhou
});
```

Event listener `async` sem `.catch()`. Se o fetch falhar (Worker down, rede), a sessao **nao e revogada no servidor** mas o usuario ve a tela de login (showLogin roda sempre). Sessao permanece ativa no D1 ate expirar (12h).

**Severidade:** MEDIUM -- Sessao nao revogada; usuario enganado sobre logout.

---

### M11: Dashboard app.js: Filter form submit cria floating promise

**Arquivo:** `infra/dashboard/assets/app.js:110-113`

```javascript
filterForm.addEventListener('submit', (event) => {
    event.preventDefault();
    refreshAll(); // async chamada sem await, sem .catch()
});
```

`refreshAll()` e async. Promise flutuante sem tratamento de rejeicao. Se `refreshAll` lancar, o status fica preso em "Atualizando dados..." permanentemente. Risco de race condition com submits rapidos.

**Severidade:** MEDIUM -- Estado de UI corrompido + race condition.

---

### M12: Dashboard app.js: main() sem .catch() no top-level

**Arquivo:** `infra/dashboard/assets/app.js:292`

```javascript
main(); // async, sem .catch()
```

Falha de inicializacao do dashboard completamente invisivel. O usuario ve HTML inicial sem estilo. Em `<script type="module">`, a rejeicao nao tratada dispara `unhandledrejection` no console mas nada visivel na UI.

**Severidade:** MEDIUM -- Dashboard pode falhar ao iniciar sem nenhum feedback.

---

### M13: Worker requestSecurity.js: readBoundedJson retorna null identico para 3 erros diferentes

**Arquivo:** `infra/cloudflare-worker/src/requestSecurity.js:25-27`

```javascript
try {
    // reader loop, decoder.decode(), JSON.parse()
} catch {
    return null; // mesmo null para: body > maxBytes, UTF-8 invalido, JSON malformado
}
```

Tres cenarios indistinguiveis produzem mesma resposta HTTP 400 "Invalid JSON". Admin debugando payload malformado nao tem como saber a causa real (tamanho, charset, sintaxe).

**Severidade:** MEDIUM -- Gap de diagnostico; erros distintos produzem mesma resposta.

---

### M14: Worker index.js: D1 queries sem try-catch (3 pontos)

**Arquivo:** `infra/cloudflare-worker/src/index.js:165,259,329`

```javascript
const { results } = await env.TELEMETRY_DB.prepare(sql).bind(...params).all();
```

Se a query D1 falhar (schema mismatch, binding error, D1 indisponivel), o erro propaga para o runtime do Workers que retorna 500. O dashboard recebe `network-error` ou `invalid-response` e mostra "Sem dados ainda" sem indicacao de erro de banco.

**Severidade:** MEDIUM -- Falha de D1 mascarada como "sem dados".

---

## LOW

### L1-L12: Empty catch blocks benignos (cleanup de finally, race de processo)

**Arquivos/Linhas:**
- `src/FiveMCleaner.Launcher/Program.cs:144-146` -- WaitForParentExit race vazia
- `src/FiveMCleaner.Updater/Program.cs:52-54` -- idem
- `src/FiveMCleaner.Updater/Program.cs:109` -- TryKill processo ja morto
- `src/FiveMCleaner.UpdateRuntime/AtomicFile.cs:62` -- finally cleanup temp file
- `src/FiveMCleaner.UpdateRuntime/UpdateRecoveryJournal.cs:63,79` -- move/delete corrupt journal
- `src/FiveMCleaner.UpdateRuntime/RuntimePackageStager.cs:59` -- finally cleanup staging dir
- `src/FiveMCleaner.App/Services/SecureFirebaseSessionStore.cs:43` -- ClearAsync delete file
- `src/FiveMCleaner.App/Services/SilentUpdateInstaller.cs:180` -- finally delete temp updater
- `src/FiveMCleaner.App/Services/SignedManifestUpdateService.cs:195` -- finally delete temp download
- `src/FiveMCleaner.App/Services/StreamingSoftwareDetector.cs:264` -- (nota: ver H2, bug real)
- `src/FiveMCleaner.App/App.xaml.cs:69-72` -- ShowAlreadyRunningMessage dialog failure

Estes sao catch blocks intencionais em cenarios de cleanup/race benigna. Nao representam risco funcional, mas a falta de diferenciacao entre excecoes esperadas e inesperadas pode ocultar falhas genuinas.

---

### L13: TrayIconService: Notifications fire-and-forget

**Arquivo:** `src/FiveMCleaner.App/Services/TrayIconService.cs:79,113`

```csharp
_ = ShowUpdateAvailableAsync(version);
_ = HideAfterDelayAsync(TimeSpan.FromSeconds(8));
```

**Severidade:** LOW -- Operacoes cosmeticas de tray icon.

---

### L14: Dashboard api.js: requestJson catch-all sem discriminacao de erro

**Arquivos:** `infra/dashboard/assets/api.js:84-86,98-100`

Design intencional (documentado) para prevenir que Promises rejeitadas congelem a UI. Trade-off aceitavel para dashboard admin.

**Severidade:** LOW -- Dashboard admin interno, nao publico.

---

### L15: Website site.js: Event listeners sem try-catch (3 pontos)

**Arquivo:** `website/public-site/site.js:17,29,42`

Pointermove, IntersectionObserver, scroll handlers sem error isolation. Codigo atual seguro (optional chaining), mas fragil a mudancas futuras.

**Severidade:** LOW -- Site de marketing, nao afeta produto.

---

### L16: Worker releaseManifest.js: Catch all no JSON.parse do manifest

**Arquivo:** `infra/cloudflare-worker/src/releaseManifest.js:35-37`

Manifest e server-side env var, nao user input. Malformacao seria bug de deploy, nao runtime.

**Severidade:** LOW -- Perda de fidelidade diagnostica apenas.

---

### L17: Worker crypto.js: verifyPassword catch base64 decode

**Arquivo:** `infra/cloudflare-worker/src/auth/crypto.js:101-103`

Fail-closed correto para seguranca. Retorna false (credenciais invalidas) em vez de expor erro interno.

**Severidade:** LOW -- Postura de seguranca correta, apenas perda operacional.

---

## Recomendacoes priorizadas

1. **H2 (StreamingSoftwareDetector)**: Corrigir `catch (Exception) when (IsExpectedProbeException(new Exception()))` para capturar a excecao real. Uma linha: `catch (AggregateException ex) when (ex.InnerExceptions.All(e => IsExpectedProbeException(e)))`

2. **H1 (FirebaseAuthService)**: Adicionar log/try-catch no `LogoutAsync` fire-and-forget para detectar falha de `ClearAsync`.

3. **M3 (UpdaterDiagnostics)**: Adicionar contador de falhas ou log estruturado nos 5 pontos de catch vazio de telemetria.

4. **M4 (MainViewModel)**: Trocar `catch` bare por `catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException))` no TrackOptimizationTelemetryAsync.

5. **M6 (MainViewModel)**: Adicionar `.ContinueWith(t => { if (t.IsFaulted) LogSilentFailure(t.Exception); }, TaskContinuationOptions.OnlyOnFaulted)` no `SaveSettingsRevisionAsync`.

6. **M10 (Dashboard)**: Adicionar try-catch no event listener do logout com mensagem de erro visivel.

7. **M9 (Worker)**: Considerar upsert com `INSERT OR REPLACE` ou coluna de idempotencia para action links, reduzindo impacto de escrita parcial.

8. **M13 (Worker)**: Diferenciar erros no `readBoundedJson` retornando codigos distintos (body-too-large, invalid-utf8, invalid-json) em vez de null unico.

---

## Escopo da auditoria

- **C#**: `src/**/*.cs`, `tests/**/*.cs` (~150 arquivos analisados)
- **JavaScript**: `infra/cloudflare-worker/src/**/*.js`, `infra/dashboard/assets/**/*.js`
- **Website**: `website/public-site/site.js`, `website/tests/` (analise superficial)
- **Nao coberto**: XAML, PowerShell scripts, config files, CI/CD

**Limites da auditoria**: Analise estatica de padroes. Nao foi exercitado runtime. Falsos positivos e falsos negativos sao possiveis. Recomenda-se revisao manual dos achados HIGH e MEDIUM antes de remediacao.

---

## Commits

Commit unico com este relatorio. Nenhuma alteracao de codigo nesta auditoria.
