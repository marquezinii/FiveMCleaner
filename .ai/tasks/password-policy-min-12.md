# Política de senha: apenas mínimo de 12 caracteres

- Agente: opencode
- Branch: `ai/opencode/password-policy-min-12`
- Status: pronta para integração
- Objetivo: o cadastro/recuperação exige somente senha com no mínimo 12 caracteres, sem
  exigir maiúscula, minúscula, número ou caractere especial, refletindo a mensagem
  "A senha deve possuir pelo menos 12 caracteres." no frontend e no backend.

## Contexto

Após a integração do Firebase Authentication REST, o fluxo de contas do Worker
(`userAccountProvider.js`) foi removido; o backend de usuários é o próprio Firebase
Auth. No cliente, `AccountPasswordPolicy` já validava apenas o comprimento, mas
mantinha campos mortos de classes de caracteres (`HasUppercase`, `HasLowercase`,
`HasNumber`, `HasSpecialCharacter`, `CompletedCount`) e mensagens desatualizadas
("12 a 128 caracteres").

## Alterações

- `src/FiveMCleaner.App/Services/AccountPasswordPolicy.cs`: `PasswordRequirements`
  reduzido a `HasMinimumLength`/`HasMaximumLength` (mínimo 12, máximo 128); os
  campos de classes de caracteres e `CompletedCount` foram removidos. `IsValid`
  agora exige apenas 12–128 caracteres.
- `src/FiveMCleaner.App/Views/AccountWindow.xaml`: dica padrão do campo de senha
  agora é "A senha deve possuir pelo menos 12 caracteres."
- `src/FiveMCleaner.App/Views/AccountWindow.xaml.cs`: mensagens de validação do
  cadastro e da alteração de senha atualizadas para refletir apenas o mínimo de 12
  caracteres; contador ao vivo passou a mostrar `N caracteres (mínimo 12)`.
- `tests/FiveMCleaner.Tests/App/FirebaseAuthServiceTests.cs`: teste da política
  reforçado para confirmar que senhas de 12 caracteres de uma única classe
  (só minúsculas, só dígitos, só maiúsculas, só símbolos) são válidas e que vazia/
  nula/<12/>128 são inválidas.

## Testes agressivos do fluxo de cadastro (segunda rodada)

Probe de `MailAddress` e revisão do fluxo revelaram e corrigiram problemas fora da
política de senha mas no caminho do cadastro:

- `src/FiveMCleaner.App/Services/AccountValidation.cs` (novo): validação de e-mail
  extraída para o serviço (`IsValidEmail`), reutilizada no cadastro, login,
  redefinição de senha e troca de e-mail; `AccountWindow.xaml.cs` não usa mais
  `System.Net.Mail` nem o helper privado `ValidEmail`.
  - Comportamento confirmado por probe: aceita `a@b`, `x@y`, `user@example` (sem
    TLD), `user@example.com.` e `user@-example.com`; rejeita espaços internos,
    `@` duplicado, começo/vazio e `user name@example.com`.
- `src/FiveMCleaner.App/Services/FirebaseAuthService.cs`: timeout do HTTP (20s)
  virava `TaskCanceledException`, era repassado e derrubava o app. Agora
  `PostAsync` e `SignInAsync` capturam `OperationCanceledException` quando o token
  de cancelamento **não** foi pedido pelo chamador e mapeiam para
  `NETWORK_REQUEST_FAILED` (mensagem amigável na UI).
- `src/FiveMCleaner.App/Services/SecureFirebaseSessionStore.cs`: `WriteAsync` agora
  é best-effort; `IOException`/`UnauthorizedAccessException`/`CryptographicException`
  (ex.: lock transitivo do arquivo, conta sem permissão) não derrubam o fluxo de
  cadastro/login — perdem o "manter conectado", nunca crasham.
- `src/FiveMCleaner.App/Services/FirebaseAuthErrorMapper.cs`: `EMAIL_EXISTS` agora
  orienta a recuperação ("Esqueci minha senha"); novo `INVALID_EMAIL`.
- `tests/FiveMCleaner.Tests/App/AccountSignUpFlowTests.cs` (novo, 30 casos):
  happy path do `RegisterAsync` (signUp → lookup → sendOobCode), não persistência
  de sessão quando `keepSignedIn=false`, mapeamento de erros Firebase (incl.
  `PASSWORD_DOES_NOT_MEET_REQUIREMENTS`), falha de rede, timeout, falha do e-mail
  de verificação (conta fica pendente), armadilha de conta (lookup falha após
  signUp e o retry orienta recuperação), cancelamento do chamador propagado e
  `WriteAsync` best-effort.

## Backend

Nenhuma mudança no Worker foi necessária: não há mais rota de cadastro de usuário
nele (removida na integração do Firebase); `passwordAuthProvider.js` trata somente
o login de admin do dashboard e não aplica política de cadastro.

**Confirmado por probe de rede real (Firebase Auth REST, projeto `fivemcleaner-app`):**
a política do servidor já está alinhada ao cliente — senha `abcdefghijkl` (12, só
minúsculas) em `accounts:signUp` retorna `idToken` (cria a conta); senha `short`
retorna `PASSWORD_DOES_NOT_MEET_REQUIREMENTS: [Password must contain at least 12
characters]` (cobra só o mínimo, sem classes de caracteres). **Nenhuma alteração no
console do Firebase é necessária.**

Outras descobertas do probe:
- `accounts:createAuthUri` aceita `abcdef@bogus-example.invalid` e `abcdef@example`
  (sem TLD) — o servidor não rejeita e-mails com domínio inválido.
- O código de erro de política retornado pelo servidor é
  `PASSWORD_DOES_NOT_MEET_REQUIREMENTS` (não `WEAK_PASSWORD`); o mapper agora
  trata ambos (defensivo — o cliente já bloqueia <12 antes de enviar).

## Validação

- `dotnet test` (666 aprovados, 0 falhas) incluindo o teste da política atualizado
  e os 30 casos agressivos do fluxo de cadastro.
- Build Release de `FiveMCleaner.App` sem avisos.
- `dotnet format FiveMCleaner.slnx --verify-no-changes --no-restore` aprovado.
- `scripts/Verify-Safety.ps1` aprovado (666 testes, 0 avisos/erros).
- `git diff --check` limpo.

## Observações para integração

- Mudança pequena e isolada; sem alteração de versão, release, instalador ou deploy.
- O atalho de desenvolvimento deve ser reconstruído na integração conforme `AI_RULES`.
- **Pré-integração:** rodar `scripts/Verify-Safety.ps1` como etapa obrigatória antes
  do merge, além de `dotnet test` completo.
