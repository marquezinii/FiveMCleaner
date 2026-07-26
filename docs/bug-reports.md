# Relatos de bug e privacidade

A tela **Relatar um bug** envia o relato somente depois de uma ação
explícita do usuário, via HTTPS, para a rota `/bugs` do mesmo Worker
Cloudflare que recebe a telemetria de uso (ver
[Telemetria opcional e privacidade](telemetry.md)). O FormSubmit não é mais
usado para relatos de bug — todo o fluxo (validação, persistência em D1 e
armazenamento do anexo) é interno à infraestrutura Cloudflare do projeto.
Não há envio periódico, telemetria em segundo plano ou repetição automática
após falha.

Vulnerabilidades não devem ser enviadas por esse formulário. Para falhas de
segurança, siga [SECURITY.md](../SECURITY.md) e use o relato privado do GitHub.

## Dados enviados

O formulário envia sempre:

- identificador aleatório do relato;
- categoria, resumo e descrição digitados;
- versão do FiveMCleaner;
- perfil selecionado.

Quando a opção de informações técnicas estiver habilitada, também envia a
descrição de versão do Windows e a edição detectada. O app não preenche nome,
e-mail, hostname, nome de usuário, caminhos locais ou servidor FiveM.

Uma imagem PNG é opcional. Antes do envio, o app valida a assinatura PNG,
limita o tamanho, decodifica e grava uma nova imagem com nome aleatório para
remover EXIF e o nome original. O Worker repete essa mesma validação
(assinatura de bytes PNG) do lado do servidor antes de gravar no bucket R2 —
nunca confia apenas na checagem feita pelo cliente. Isso não remove
informações visíveis nos pixels: notificações, nomes, chats, endereços, IDs e
outras áreas pessoais devem ser ocultados pelo próprio usuário antes da
seleção.

## Onde os dados ficam

O relato (categoria, resumo, descrição, versão, perfil, resumo técnico e
ambiente) é gravado na tabela `bug_reports` do D1 do Worker. O anexo, quando
enviado, é gravado no bucket R2 `fivemcleaner-bug-reports`, referenciado pela
coluna `attachment_key`. Nenhum dos dois é público: o painel administrativo
lista os relatos (aba **"Bugs reportados"**) e serve o anexo através de um
endpoint autenticado (`/api/bugs/:id/attachment`), atrás da mesma senha de
administrador usada para o resto do painel.

Não inclua senhas, tokens, cookies, entitlement, dumps, ETW traces,
conteúdo de chat ou qualquer dado que não aceitaria encaminhar a um terceiro.

O botão **Copiar relato** cria texto no clipboard e não envia a imagem. O
conteúdo pode então ser revisado e publicado manualmente no
[formulário de bug do GitHub](https://github.com/marquezinii/FiveMCleaner/issues/new?template=bug_report.yml).

## Estado da entrega

O código do transporte, da validação server-side e do armazenamento em R2
está completo e testado (`infra/cloudflare-worker/src/bugReports/`), mas a
rota `/bugs` e o bucket R2 exigem um redeploy explícito do Worker
(`wrangler deploy`) e a criação do bucket (`wrangler r2 bucket create
fivemcleaner-bug-reports`) antes de funcionarem de fato — nenhum dos dois foi
executado ainda. Até esse redeploy acontecer, o botão **Enviar relato** falha
com uma mensagem clara em vez de silenciosamente cair de volta para o
FormSubmit, que foi removido do código.
