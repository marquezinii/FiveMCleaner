# Bug Hunt Fix — Correções de conta e sessão

- **Agente**: claude
- **Branch**: ai/claude/bug-hunt-fix
- **Objetivo**: Corrigir 9 bugs encontrados na janela de conta e sessão Firebase
- **Status**: pronto para integração

## Resumo das mudanças

### MainWindow.xaml
- Adicionado x:Name a 4 botões do card de configurações de conta (senha, email, excluir, sair)

### MainWindow.Account.xaml.cs
- `SetAccountSettingsBusy`: agora desabilita todos os 6 botões durante operações async (antes só desabilitava ChangePhotoButton)
- `AccountSettingsStatus`: Background usa SetResourceReference com brushes semi-transparentes reativos ao tema

### AccountWindow.xaml
- Adicionado x:Name aos botões de verificação (ResendVerificationButton, RefreshVerificationButton)

### AccountWindow.xaml.cs
- `Dispatcher.Invoke` → `Dispatcher.InvokeAsync` (evita deadlock)
- `SetBusy`: agora também desabilita botões de verificação
- `ResetPassword_Click`: adicionado SetBusy proteção double-click
- `DialogResult = true` → `CloseAfterSignIn()` (3 ocorrências, com try/catch)
- LogoutButton visível mesmo em `requiresProfileSetup` (usuário pode sair/retry)
- `Status`: Background usa SetResourceReference reativo ao tema

### FirebaseAuthService.cs
- Adicionado `SemaphoreSlim` para serializar RefreshAsync, AcceptTokensAsync e LogoutAsync
- `LogoutAsync` agora verifica `Current.State == SignedOut` para prevenir double logout
- Extraído `LogoutCoreAsync` para uso interno

### ThemeManager.cs
- Adicionados `AccountStatusErrorBackgroundBrush` e `AccountStatusSuccessBackgroundBrush` (semi-transparentes) aos paletes Dark e Light

## Testes
- dotnet test: 774/774 aprovados
- dotnet format --verify-no-changes: aprovado
- Build Release: sem avisos

## Commits
- (a ser criado)

## Worker Backend — Correções de segurança e hardening

### cors.js
- `Access-Control-Allow-Headers` agora inclui `Authorization` (necessário para `/account/profile`)

### index.js
- Rate limiting adicionado em `handleTelemetryIngest` (`TELEMETRY_LIMITER`), `handleBugReportIngest` (`BUG_REPORT_LIMITER`) e `handleUpdaterEventIngest` (`UPDATER_EVENT_LIMITER`)
- Telemetry queries usam `INSERT OR IGNORE` para idempotência
- Falha parcial de chunk de telemetria loga e continua (não retorna 500)
- Sanitização do nome de arquivo CSV (`[^a-zA-Z0-9_-]` → `_`)

### passwordAuthProvider.js
- `getSession`, `saveSession` e `revokeSession` agora armazenam SHA-256 do session ID no D1 (antes plaintext)
- Bloco de falha de login usa UPDATE atômico (`INSERT ... ON CONFLICT DO UPDATE`) em vez de read-modify-write (fecha TOCTOU no brute-force guard)

### sessionStore.js
- Nova função exportada `hashToken` (wrapper de `hashSessionId`)

### csv.js
- Regex de CSV injection ampliada com caracteres Unicode fullwidth (`＝`, `＋`, `＃`) e pipe (`|`)

### filters.js
- Nova validação ISO 8601 para parâmetros `from`/`to`

### Testes
- Worker: 159/159 aprovados