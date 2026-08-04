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

## Backend

Nenhuma mudança no Worker foi necessária: não há mais rota de cadastro de usuário
nele (removida na integração do Firebase); `passwordAuthProvider.js` trata somente
o login de admin do dashboard e não aplica política de cadastro.

**Pendência fora do repositório (não executável por este agente):** a política de
senha do servidor é configurada no console do Firebase (Authentication / Identity
Platform) para o projeto `fivemcleaner-app`. Para refletir a mesma regra no
servidor, ajustar lá para: comprimento mínimo 12 e **sem** exigir maiúscula,
minúscula, número ou símbolo. Sem isso, o Firebase pode continuar rejeitando com
`WEAK_PASSWORD` senhas que o cliente aceite.

## Validação

- `dotnet test` (636 aprovados, 0 falhas) incluindo o teste atualizado.
- Build Release de `FiveMCleaner.App` sem avisos.
- `dotnet format FiveMCleaner.slnx --verify-no-changes --no-restore` aprovado.
- `scripts/Verify-Safety.ps1` aprovado (636 testes, 0 avisos/erros).
- `git diff --check` limpo.

## Observações para integração

- Mudança pequena e isolada; sem alteração de versão, release, instalador ou deploy.
- O atalho de desenvolvimento deve ser reconstruído na integração conforme `AI_RULES`.
