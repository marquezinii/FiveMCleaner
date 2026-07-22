# Relatos de bug e privacidade

A tela **Relatar um bug** da `v0.2.0-preview` envia o relato somente depois de
uma ação explícita do usuário. O envio usa o endpoint HTTPS AJAX do
[FormSubmit](https://formsubmit.co/ajax-documentation), um serviço externo que
encaminha o conteúdo ao mantenedor por e-mail. Não há envio periódico,
telemetria em segundo plano ou repetição automática após falha.

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

Uma imagem PNG/JPEG é opcional. Antes do envio, o app valida o formato, limita a
8 MB, decodifica e grava uma nova imagem PNG com nome aleatório para remover
EXIF e o nome original. Isso não remove informações visíveis nos pixels:
notificações, nomes, chats, endereços, IDs e outras áreas pessoais devem ser
ocultados pelo próprio usuário antes da seleção.

## Terceiro envolvido

Ao pressionar **Enviar relato**, os dados saem do computador e são processados
pelo FormSubmit. Como em qualquer conexão HTTPS, o serviço e a infraestrutura
de rede podem observar dados técnicos da conexão, como endereço IP. Consulte a
[política de privacidade do FormSubmit](https://formsubmit.co/privacy.pdf)
antes de enviar.

Não inclua senhas, tokens, cookies, entitlement, dumps, ETW traces,
conteúdo de chat ou qualquer dado que não aceitaria encaminhar a um terceiro.

O botão **Copiar relato** cria texto no clipboard e não envia a imagem. O
conteúdo pode então ser revisado e publicado manualmente no
[formulário de bug do GitHub](https://github.com/marquezinii/FiveMCleaner/issues/new?template=bug_report.yml).

## Estado da entrega

O endereço destinatário foi ativado em 18 de julho de 2026 seguindo o fluxo
oficial do FormSubmit. O aplicativo usa o identificador opaco fornecido pelo
serviço, e não publica o endereço de e-mail no endpoint.

O teste ponta a ponta foi repetido depois da ativação com um relato sintético,
sem anexo nem dados pessoais:

- a API retornou `success: true`;
- o e-mail chegou ao destinatário correto;
- o identificador do relato recebido coincidiu com o enviado;
- nenhum nome, e-mail ou mídia foi incluído.

O artefato final ainda deve repetir um smoke sem envio para validar a abertura e
o cancelamento da janela. Alterar o destinatário ou o domínio de origem exige
uma nova ativação conforme a [central de ajuda](https://formsubmit.co/help).
