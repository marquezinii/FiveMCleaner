# Cadastro com Nome, Sobrenome e Usuário único

- **Agente:** Claude (sessão integradora, autorizada a trabalhar direto em
  `dev/proxima-versao`)
- **Objetivo:** repor Nome, Sobrenome e um nome de usuário único no cadastro,
  que a migração para Firebase Authentication REST havia removido (Firebase
  só administra e-mail/senha/uid), na ordem pedida pelo usuário: Nome +
  Sobrenome lado a lado, Usuário, E-mail, Senha, Repetir senha.
- **Status:** pronto para integração (já integrado nesta sessão).

## Decisão de arquitetura

Firebase Authentication REST não tem conceito de username nem de nome/
sobrenome — só e-mail, senha e uid. Duas opções:

1. Guardar esses campos só no cliente (ex.: `displayName` do Firebase) — mas
   aí a unicidade do nome de usuário não pode ser garantida (qualquer
   instalação poderia "reivindicar" o mesmo nome sem checagem central).
2. Um passo server-side de conclusão de perfil, autenticado pelo ID Token
   Firebase já verificado pelo Worker (`requireFirebaseUser`, integrado na
   rodada anterior), guardando os campos extras numa tabela própria indexada
   pelo UID.

Escolhida a opção 2: é a única que cumpre o requisito "não pode repetir
usuário" de verdade — usernames são um recurso *compartilhado* entre todas
as instalações do app, então só uma fonte de verdade central (o Worker + D1)
pode arbitrar unicidade sob concorrência.

## Mudanças — Worker (`infra/cloudflare-worker`)

- `src/auth/accountProfile.js`: `validateAccountProfile` (username 3–24
  caracteres, começa com letra, `[a-zA-Z0-9_]`; nome/sobrenome 1–60
  caracteres, letras Unicode/espaço/hífen/apóstrofo) e
  `createAccountProfile` (INSERT com uid do token verificado, nunca do
  corpo da requisição; mapeia violação de unicidade para `username-taken`).
- `src/index.js`: nova rota `POST /account/profile`, atrás de
  `requireFirebaseUser` — 400 em payload inválido, 409
  `{ error: "username-taken" }` em conflito, 201 em sucesso.
- `schema.sql` + `migrations/0002_account_profiles.sql`: tabela
  `account_profiles` (uid PK, username, username_normalized único,
  first_name, last_name, created_at).
- `test/auth/accountProfile.test.js`: 16 testes novos (validação pura +
  mapeamento de erro do D1 com um `db` fake, sem precisar de Miniflare).
- `README.md`/`docs/architecture.md` atualizados.

## Mudanças — App (`src/FiveMCleaner.App`)

- `Services/AccountValidation.cs`: `IsValidUsername`/`IsValidPersonName`
  (mesmas regras do Worker — checagem client-side é só UX, o Worker
  revalida tudo).
- `Services/AccountProfileService.cs` +
  `Services/CloudflareAccountProfileService.cs`: chamada HTTPS autenticada
  (`Authorization: Bearer <idToken>`) a `POST /account/profile`, com
  `DisabledAccountProfileService` como fallback honesto se o endpoint não
  estiver configurado (mesmo padrão de `BugReportService`).
- `Services/RemoteServicesOptions.cs` + `Config/appsettings*.json`:
  `AccountProfileEndpoint`.
- `Views/AccountWindow.xaml`/`.xaml.cs`: Nome+Sobrenome lado a lado, Usuário,
  E-mail, Senha, Repetir senha, nessa ordem, visíveis só no cadastro. Se o
  Firebase criar a conta mas o perfil falhar ao salvar (username já em uso,
  ou falha de rede), a conta Firebase já criada **não é descartada**: a
  janela estreita para só os campos de perfil (`requiresProfileSetup`) até o
  usuário escolher um nome de usuário disponível.
- `AccountSignUpFlowTests.cs` (14 testes novos de validação) e
  `CloudflareAccountProfileServiceTests.cs` (5 testes novos) cobrindo
  sucesso, 409, 500, falha de rede e endpoint não-HTTPS.

## Testes

Build Release sem avisos, 719 testes .NET (29 novos), `dotnet format
--verify-no-changes`, `Verify-Safety.ps1`, 148 testes do Worker (16 novos),
`git diff --check` aprovados.
