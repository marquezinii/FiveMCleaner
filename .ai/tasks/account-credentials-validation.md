# Validação de credenciais da conta

- Agente: Codex
- Branch: `ai/codex/account-credentials-validation`
- Status: integrada em `dev/proxima-versao`
- Objetivo: corrigir o fechar da janela de conta e reforçar o cadastro com validações claras, senha forte e visibilidade de senha.

## Alterações

- O botão de fechar agora desenha um X vetorial e fica junto ao canto superior direito.
- Sobrenome é opcional; nome, usuário, e-mail, senha, confirmação e termos continuam obrigatórios.
- Cadastro mostra requisitos de senha, medidor de força, validação de confirmação e controles de mostrar/ocultar senha.
- Cliente e Worker aplicam a mesma política de senha: 12+ caracteres, maiúscula, minúscula, número e caractere especial.
- O erro informa o campo exato e foca nele. O Worker também aceita sobrenome vazio e rejeita requisitos ausentes, inclusive termos.

## Validação

- `dotnet restore tests/FiveMCleaner.Tests/FiveMCleaner.Tests.csproj`
- `dotnet test tests/FiveMCleaner.Tests/FiveMCleaner.Tests.csproj --no-restore --filter FullyQualifiedName~UserAccountServiceTests` — 3 aprovados.
- `dotnet test tests/FiveMCleaner.Tests/FiveMCleaner.Tests.csproj --no-restore` — aprovado.
- `npm test` em `infra/cloudflare-worker` — aprovado.

## Limitação conhecida

Não há remetente/provedor de e-mail configurado no repositório. A sintaxe é validada, mas confirmação de que uma caixa postal existe exige envio de link/código por e-mail e não foi simulada.

## Integração

- Integrada em `dev/proxima-versao` pelo merge `e41a7e3`.
