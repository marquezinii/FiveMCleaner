# Bug Hunt & Audit: Consentimento + Updater

- **Agente**: opencode
- **Branch**: `ai/opencode/bug-hunt-fix`
- **Objetivo**: Corrigir bugs de consentimento e auditoria do updater
- **Status**: pronto para integracao

## Rodada 1: Preferencias de consentimento ignoradas

Foram encontrados 6 bugs onde `telemetry.SetEnabled(true)` e `shareCrashReports = true` estavam hardcoded em vez de usar o valor real da preferencia do usuario:

### Bugs corrigidos

1. **`MainViewModel.ShareAnonymousTelemetry` setter** (`MainViewModel.cs:464`): `telemetry.SetEnabled(true)` → `telemetry.SetEnabled(value)`
2. **`MainViewModel.ApplySettings`** (`MainViewModel.cs:1543`): `telemetry.SetEnabled(true)` → `telemetry.SetEnabled(shareAnonymousTelemetry)`
3. **`MainViewModel.ApplySettings`** (`MainViewModel.cs:1544`): `shareCrashReports = true` → `shareCrashReports = settings.ShareCrashReports`
4. **`PrivacyConsentOutcomeBuilder.BuildConfirmed`** (`PrivacyConsentOutcomeBuilder.cs:26`): `ShareCrashReports = true` → `ShareCrashReports = acceptCrashReports`
5. **`MainViewModel.ConfirmPrivacyConsentAsync`** (`MainViewModel.cs:1706`): `telemetry.SetEnabled(true)` → `telemetry.SetEnabled(snapshot.ShareAnonymousTelemetry)`
6. **`MainViewModel.ConfirmPrivacyConsentAsync`** (`MainViewModel.cs:1707`): `shareCrashReports = true` → `shareCrashReports = snapshot.ShareCrashReports`

## Rodada 2: Auditoria do updater (Launcher, Updater, Manifest, Staging, Recovery)

### Auditoria completa dos arquivos

- **Launcher/Program.cs**: fluxo transacional, WaitForParent, health-check, recovery, rollback
- **Updater/Program.cs**: legacy Inno Setup path, VerifyInstaller, RunInstaller
- **UpdateHandoff.cs**: parsing, validacao de caminhos
- **SignedManifestUpdateService.cs**: validacao de manifesto, download com hash, redirect handling
- **AtomicUpdateInstaller.cs**: staging, ativacao, rollback imediato
- **SilentUpdateInstaller.cs**: legacy installer, copia e verificacao do updater
- **RecoveryCoordinator.cs**: Reconcile, Abandon
- **RuntimePackageStager.cs**: extracao ZIP, verificacao SHA256SUMS.txt, anti-path-traversal
- **RuntimeActivationStore.cs**: active.json atomico com retry
- **UpdateRecoveryJournal.cs**: recovery.json, MarkCandidateLaunched, Complete
- **UpdateHealthReceiptStore.cs**: health.json, confirmacao de nonce
- **VersionFloorStore.cs**: piso anti-downgrade com DPAPI
- **AtomicFile.cs**: escrita atomica com fallback File.Move
- **TransientRetry.cs**: retry curto para locks transitórios
- **UpdaterDiagnostics.cs**: log JSONL, fila de telemetria, IsTelemetryAuthorized
- **Worker updaterEvents/validateSubmission.js**: validacao server-side
- **Worker updaterEvents/queries.js**: queries parametrizadas

### Bug encontrado e corrigido

**`Launcher/Program.cs`**: `telemetryAuthorized: true` hardcoded em todos os `RecordAsync` e `FlushPendingAsync`. O método `UpdaterDiagnostics.IsTelemetryAuthorized()` (que le settings.json) existia mas nunca era chamado pelo Launcher.

- Agora o Launcher le `IsTelemetryAuthorized(dataRoot)` no inicio e passa o valor real para todas as chamadas de telemetria.
- Quando o usuario desativa telemetria, eventos de updater pendentes sao deletados em vez de enviados.

### Areas verificadas sem bugs

- Validacao de manifesto assinado (ECDSA P-256/SHA-256, anti-downgrade, URL allowlist) — solida
- Download com hash (streaming SHA-256, limite de tamanho, redirect seguro) — solido
- Staging ZIP (SHA256SUMS.txt fechado, anti-path-traversal, anti-zip-bomb) — solido
- Recovery (health receipt com nonce, timeout, Abandon vs Reconcile) — solido
- Journal/activation atomicos (AtomicFile + TransientRetry) — solido
- Legacy Updater (PID+start-time anti-reuse, file handle lock, TOCTOU hash duplo) — solido
- Worker validation (closed schema, environment allowlist, version regex) — solido

### Validacao

- **636 testes .NET**: aprovados (0 falhas)
- **104 testes updater**: aprovados
- **Verify-Safety.ps1**: aprovado
- **Build Release**: sem avisos, sem erros
