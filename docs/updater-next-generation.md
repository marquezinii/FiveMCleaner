# Arquitetura do atualizador de próxima geração

## Decisão

Substituir gradualmente o setup Inno e o updater próprio por distribuição
`MSIX/App Installer`, assinada por certificado de publicação estável. O
Windows torna-se o responsável por staging, registro, instalação, reparo e
troca de versão. Um **Recovery Agent** pequeno e assinado permanece separado
do pacote principal apenas para confirmar a saúde pós-atualização e solicitar
o retorno à versão anterior quando necessário.

Não usar pinning de certificado TLS da Cloudflare ou do GitHub. Certificados
de borda podem ser rotacionados legitimamente. A cadeia de confiança terá dois
controles independentes: TLS padrão do Windows, com validação e revogação, e
assinatura de código/manifesto com chave pública fixa do FiveMCleaner.

## Cadeia de confiança

1. O usuário instala um `.appinstaller` HTTPS que referencia um
   `FiveMCleaner.msixbundle` assinado.
2. O Windows valida a assinatura Authenticode/MSIX contra o Publisher fixo no
   manifesto do pacote. O certificado de produção deve ser de reputação
   confiável; certificados de teste nunca entram no canal público.
3. O feed de release é um documento canônico, assinado com Ed25519. O app e o
   Recovery Agent contêm somente a chave pública de produção e rejeitam feed,
   versão, URLs, hashes e assinatura inválidos.
4. O feed lista tamanho, SHA-256, versão, canal, hash do bundle, URL HTTPS
   allowlisted e a versão anterior recuperável. A chave privada fica fora do
   repositório, em segredo de release com acesso mínimo e rotação planejada.
5. TLS usa o validador nativo do Windows, SNI/hostname e revogação online. Não
   há callback permissivo de certificado, redirecionamento livre ou fallback
   HTTP.

## Atualização e rollback verificáveis

O pacote novo é baixado e estagiado antes de substituir a ativação atual. O
agente registra uma transação local com: versão anterior, versão candidata,
hashes do feed, momento e estado. Após a primeira abertura, o app deve gravar
um *health receipt* autenticado localmente somente depois de inicializar UI,
configuração e serviços essenciais.

Se não houver receipt dentro do prazo ou o novo processo encerrar repetidamente,
o Recovery Agent instala a versão anterior listada no feed assinado, confirma a
versão ativada pelo Windows e marca a candidata como bloqueada até existir uma
release posterior. O evento de rollback é persistido localmente antes de
qualquer telemetria.

Dados do usuário não são tratados como atômicos por MSIX. Cada migração de
dados deve ser versionada, journaling e reversível; a troca do ponteiro de
dados ocorre apenas após o health receipt. Migrações irreversíveis são
proibidas em uma atualização automática.

## Telemetria do updater

O Recovery Agent mantém log local detalhado, rotacionado e sem dados pessoais.
Para o Worker, envia apenas evento estruturado e limitado para `POST
/updater-events`: versão anterior/candidata, fase, código de erro, categoria,
timestamp e ambiente. O envio depende do mesmo consentimento de telemetria do
app; texto livre, caminhos, dumps e logs completos nunca deixam o PC.

O Worker valida o schema, limite e origem, persiste em tabela D1 própria e
oferece `GET /api/updater-events` exclusivamente sob sessão administrativa. O
dashboard ganha a aba **Bugs do updater**, sem expor essa URL em conteúdo
público. Os detalhes completos continuam acessíveis somente no log local que o
usuário escolhe compartilhar.

## Migração sem ruptura

1. Criar pipeline de assinatura, feed Ed25519 e validação independente em CI.
2. Empacotar MSIX/App Installer em canal interno e testar instalação, repair,
   upgrade, queda de energia simulada e rollback em VMs limpas.
3. Publicar um instalador de transição assinado que detecta Inno, preserva
   dados e registra a instalação MSIX; nunca tentar converter silenciosamente
   uma instalação existente.
4. Manter Inno somente para reparo/legado durante janela definida, sem novos
   recursos de update. Removê-lo depois de métricas de migração e suporte.
5. Só então ligar a atualização automática para o canal estável, com rollout
   progressivo e possibilidade de pausar o feed assinado.

## Critérios de aceite

- chave privada não existe no repositório, artefato ou máquina de usuário;
- bundle, feed, versão e hash inválidos não são instalados;
- perda de processo, rede ou energia não ativa pacote parcialmente estagiado;
- falha pós-atualização reativa a versão anterior e deixa evidência local;
- nenhuma migração automática torna dados incompatíveis com rollback;
- dashboard recebe somente eventos consentidos e sanitizados;
- instalação inicial, atualização, repair e rollback passam em Windows limpo.
