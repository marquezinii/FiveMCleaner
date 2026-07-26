# Relatos de bug e privacidade

A tela **Relatar um bug** envia o relato somente depois de uma ação
explícita do usuário, via HTTPS, para a rota `/bugs` do mesmo Worker
Cloudflare que recebe a telemetria de uso (ver
[Telemetria opcional e privacidade](telemetry.md)). O FormSubmit não é mais
usado para relatos de bug — todo o fluxo (validação e persistência) é
interno à infraestrutura Cloudflare do projeto (Worker + D1). Não há envio
periódico, telemetria em segundo plano ou repetição automática após falha.

O relato é **somente texto**: não existe mais anexo/captura de tela nesse
formulário. Essa funcionalidade foi removida de propósito depois que o R2
(o armazenamento de objetos que guardaria a imagem) se mostrou exigir uma
ativação de conta pelo painel da Cloudflare (aceite de termos/possível
confirmação de billing) que não fazia sentido só para um anexo opcional —
preferimos manter o relato inteiramente dentro do D1, sem essa dependência.

Vulnerabilidades não devem ser enviadas por esse formulário. Para falhas de
segurança, siga [SECURITY.md](../SECURITY.md) e use o relato privado do GitHub.

## Dados enviados

O formulário envia sempre:

- identificador aleatório do relato;
- categoria, resumo e descrição digitados;
- versão do FiveMCleaner;
- perfil selecionado.

Opcionalmente, o usuário também pode informar:

- **e-mail** — nunca obrigatório, só para o caso de precisarmos fazer uma
  pergunta de acompanhamento sobre aquele relato específico;
- **trecho de log em texto puro** — limitado a 100 KB, validado tanto no
  app quanto novamente no Worker antes de gravar.

Quando a opção de informações técnicas estiver habilitada, também envia a
descrição de versão do Windows e a edição detectada. O app não preenche
nome, hostname, nome de usuário, caminhos locais ou servidor FiveM — o
e-mail é o único dado de contato, e só existe se o próprio usuário digitá-lo.

## Onde os dados ficam

O relato completo (categoria, resumo, descrição, versão, perfil, resumo
técnico, e-mail e trecho de log) é gravado apenas na tabela `bug_reports`
do D1 do Worker — não há bucket de armazenamento de arquivos nem qualquer
outro serviço de terceiros envolvido. O painel administrativo lista os
relatos na aba **"Bugs reportados"**, atrás da mesma senha de administrador
usada para o resto do painel.

Não inclua senhas, tokens, cookies, entitlement, dumps completos de ETW ou
qualquer dado que não aceitaria encaminhar a um terceiro — isso vale tanto
para a descrição quanto para o trecho de log opcional.

O botão **Copiar relato** cria texto no clipboard com todos os campos
preenchidos, incluindo e-mail e log quando fornecidos. O conteúdo pode
então ser revisado e publicado manualmente no
[formulário de bug do GitHub](https://github.com/marquezinii/FiveMCleaner/issues/new?template=bug_report.yml).

## Estado da entrega

A rota `/bugs` (ingestão) e `/api/bugs` (listagem autenticada para o
painel) estão **implantadas e testadas em produção** no mesmo Worker que já
servia `/telemetry`
(`https://fivemcleaner-telemetry.felipemarquesini10.workers.dev`), com a
tabela `bug_reports` do D1 já migrada (colunas `email`/`log_text`, sem
`attachment_key`). Um envio sintético de ponta a ponta foi validado após o
deploy (HTTP 202, linha conferida no D1 e removida em seguida). O app já
aponta `RemoteServicesOptions.BugReportEndpoint` para essa rota — o recurso
só passa a valer para quem usa o app quando a próxima versão pública for
lançada e instalada, já que quem está com uma versão anterior instalada
continua rodando o código antigo até atualizar.
