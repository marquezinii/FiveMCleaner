# Arquitetura do atualizador de próxima geração

## Decisão reavaliada: custo zero e durabilidade

Não migrar para MSIX/App Installer no canal público gratuito. MSIX exige que
o certificado do pacote seja confiável em cada PC; com certificado gratuito
self-signed isso cria uma etapa administrativa manual. Um certificado de
produção confiável ou serviço de assinatura tem custo, portanto não atende ao
requisito de custo zero.

Substituir gradualmente Inno e o updater atual por uma distribuição própria
versionada, composta por um **Launcher/Recovery Agent** e diretórios de app
imutáveis. Ambos são self-contained .NET, sem serviço, driver, elevação,
dependência comercial ou instalação de certificado. GitHub Releases hospeda
os pacotes e o Worker/Pages existente hospeda o feed e a observabilidade nos
planos gratuitos, sujeitos aos limites documentados desses provedores.

Não usar pinning de certificado TLS da Cloudflare ou do GitHub. Certificados
de borda podem ser rotacionados legitimamente. A cadeia de confiança terá dois
controles independentes: TLS padrão do Windows, com validação e revogação, e
assinatura de código/manifesto com chave pública fixa do FiveMCleaner.

## Cadeia de confiança

1. O instalador de transição ou download inicial instala somente o
   `FiveMCleaner.Launcher.exe` e uma versão conhecida em diretório por usuário.
2. O feed de release é um documento canônico, assinado com Ed25519. O app e o
   Recovery Agent contêm somente a chave pública de produção e rejeitam feed,
   versão, URLs, hashes e assinatura inválidos.
3. O feed lista tamanho, SHA-256, versão, canal, hash do bundle, URL HTTPS
   allowlisted e a versão anterior recuperável. A chave privada fica fora do
   repositório, em segredo de release com acesso mínimo e rotação planejada.
4. TLS usa o validador nativo do Windows, SNI/hostname e revogação online. Não
   há callback permissivo de certificado, redirecionamento livre ou fallback
   HTTP.

### Proteção contra downgrade

O feed assinado também declara `minimumAllowedVersion` por canal. O Launcher
nunca ativa, baixa ou instala uma versão menor que a ativa ou menor que esse
piso, mesmo que alguém entregue um manifesto antigo, substitua a URL ou tente
abrir um pacote local. O estado local registra a maior versão já confirmada e
o hash do feed que a autorizou; esse estado é protegido por DPAPI do usuário e
é reconciliado apenas com um feed cuja assinatura seja válida.

Rollback não é um downgrade genérico: é uma transação limitada ao par
`previousVersion` registrado antes da ativação da candidata, dentro de uma
janela de recuperação curta e com journal/health receipt correspondente. O
Recovery Agent não aceita retornar a uma versão abaixo de
`minimumAllowedVersion`; se a única versão anterior estiver revogada, ele
mantém a versão atual, registra a falha e exige uma release corretiva mais
nova. Assim, uma correção de segurança pode elevar o piso sem perder a
capacidade de recuperar uma atualização comum que falhou.

## Atualização e rollback verificáveis

O pacote novo é baixado em `staging/<transaction-id>`, recebe hash do bundle e
hash por arquivo, e só então é extraído para `versions/<version>`. A versão em
uso nunca é alterada. O Launcher troca um único ponteiro `active.json` por
`File.Replace`, depois de registrar uma transação local com versão anterior,
candidata, hashes do feed, momento e estado. Esta é a atomicidade relevante:
uma inicialização vê a versão anterior inteira ou a nova inteira, nunca uma
árvore parcialmente copiada.

Após a primeira abertura, o app grava um *health receipt* com nonce da
transação somente depois de inicializar UI, configuração, broker compatível e
serviços essenciais.

Se não houver receipt dentro do prazo ou o novo processo encerrar repetidamente,
o Recovery Agent restaura atomicamente o ponteiro para a versão anterior já
verificada no disco, confirma a versão ativada pelo Launcher e marca a candidata como bloqueada até existir uma
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

1. Criar pipeline de assinatura Ed25519, feed e validação independente em CI.
2. Construir Launcher/Recovery Agent e testar instalação, repair,
   upgrade, queda de energia simulada e rollback em VMs limpas.
3. Publicar um instalador de transição que detecta Inno, preserva
   dados e registra a primeira versão imutável; nunca tentar converter silenciosamente
   uma instalação existente.
4. Manter Inno somente para reparo/legado durante janela definida, sem novos
   recursos de update. Removê-lo depois de métricas de migração e suporte.
5. Só então ligar a atualização automática para o canal estável, com rollout
   progressivo e possibilidade de pausar o feed assinado.

## Critérios de aceite

- chave privada não existe no repositório, artefato ou máquina de usuário;
- bundle, feed, versão e hash inválidos não são instalados;
- uma versão abaixo da ativa ou de `minimumAllowedVersion` não é ativada;
- rollback só pode retornar ao predecessor registrado e nunca atravessa o
  piso de segurança assinado;
- perda de processo, rede ou energia não ativa pacote parcialmente estagiado;
- falha pós-atualização reativa a versão anterior e deixa evidência local;
- nenhuma migração automática torna dados incompatíveis com rollback;
- dashboard recebe somente eventos consentidos e sanitizados;
- instalação inicial, atualização, repair e rollback passam em Windows limpo.

## Compatibilidade com otimizações

As otimizações não dependem de uma pasta fixa do aplicativo: atuam em FiveM,
GTA V e configurações do Windows. Mesmo assim, o runtime novo deve preservar
sem alteração `%LOCALAPPDATA%\FiveMCleaner` para configurações, consentimento,
journals, rollback das otimizações e logs. Somente binários passam para
`Runtime\versions`; dados mutáveis ficam em `Data`, com contrato de migração
reversível. O broker é distribuído junto de cada versão e iniciado apenas pelo
Launcher da mesma versão, preservando seu contrato tipado e evitando mistura de
binários de versões diferentes.
