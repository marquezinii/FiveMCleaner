# Bug Hunt: Preferencias de consentimento ignoradas

- **Agente**: opencode
- **Branch**: `ai/opencode/bug-hunt-fix`
- **Objetivo**: Corrigir bugs onde preferencias de telemetria e crash reports eram ignoradas (hardcoded `true`)
- **Status**: pronto para integracao

## Resumo

Foram encontrados 6 bugs onde `telemetry.SetEnabled(true)` e `shareCrashReports = true` estavam hardcoded em vez de usar o valor real da preferencia do usuario:

### Bugs corrigidos

1. **`MainViewModel.ShareAnonymousTelemetry` setter** (`MainViewModel.cs:464`): `telemetry.SetEnabled(true)` → `telemetry.SetEnabled(value)`
   - Ao alternar o toggle de telemetria, o servico interno SEMPRE era habilitado, ignorando a escolha do usuario.

2. **`MainViewModel.ApplySettings`** (`MainViewModel.cs:1543`): `telemetry.SetEnabled(true)` → `telemetry.SetEnabled(shareAnonymousTelemetry)`
   - Na inicializacao, a telemetria era SEMPRE reabilitada, mesmo que o usuario a tivesse desativado na sessao anterior.

3. **`MainViewModel.ApplySettings`** (`MainViewModel.cs:1544`): `shareCrashReports = true` → `shareCrashReports = settings.ShareCrashReports`
   - O toggle de crash reports era SEMPRE resetado para `true` ao carregar configuracoes.

4. **`PrivacyConsentOutcomeBuilder.BuildConfirmed`** (`PrivacyConsentOutcomeBuilder.cs:26`): `ShareCrashReports = true` → `ShareCrashReports = acceptCrashReports`
   - O parametro `acceptCrashReports` era recebido mas descartado; sempre persistia `true`.

5. **`MainViewModel.ConfirmPrivacyConsentAsync`** (`MainViewModel.cs:1706`): `telemetry.SetEnabled(true)` → `telemetry.SetEnabled(snapshot.ShareAnonymousTelemetry)`
   - Apos a tela de consentimento, telemetria era SEMPRE habilitada.

6. **`MainViewModel.ConfirmPrivacyConsentAsync`** (`MainViewModel.cs:1707`): `shareCrashReports = true` → `shareCrashReports = snapshot.ShareCrashReports`
   - Apos a tela de consentimento, crash reports eram SEMPRE habilitados.

### Testes afetados e corrigidos

- `PrivacyConsentTests.BuildConfirmed_BothDeclined_SetsBothFalseAndStampsCurrentVersion`: `Assert.True` → `Assert.False`
- `PrivacyConsentTests.BuildConfirmed_OnlyTelemetryAccepted_KeepsCrashReportsFalse`: `Assert.True` → `Assert.False`
- `MainViewModelPrivacyConsentTests.ConfirmPrivacyConsentAsync_ClosingIsPassedAsBothFalse`: 3 asserts corrigidos
- `MainViewModelPrivacyConsentTests.ConfirmPrivacyConsentAsync_OnlyTelemetryAccepted`: 1 assert corrigido

### Validacao

- **636 testes .NET**: aprovados (0 falhas)
- **120 testes Worker**: aprovados (0 falhas)
- **43 testes Dashboard**: aprovados (0 falhas)
- **Verify-Safety.ps1**: aprovado
- **Build Release**: sem avisos, sem erros

### Areas revisadas sem bugs encontrados

- Worker Cloudflare: routing, validacao, SQL queries, auth, CORS, batch chunking, CSV
- Dashboard: API layer, renderizacao, login/logout
- Website: landing page
- C# Broker: ElevatedBrokerClient, ReadUntilTerminalAsync
