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