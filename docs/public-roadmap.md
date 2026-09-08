# Roadmap público

`public-roadmap.json` é a fonte de verdade do Roadmap exibido em
`https://vemryx.com/Ralven/`. Ele não é changelog e não é uma lista automática
de commits.

Atualize-o no mesmo pull request que integra uma capacidade pronta para ser
comunicada publicamente na próxima versão. Cada item exige `id` estável e
textos não vazios em PT e EN para `status`, `title` e `description`.

Somente inclua mudanças já integradas em `dev/proxima-versao`, sem datas,
promessas de desempenho ou recursos especulativos. Ao ser alterado nessa
branch, o workflow `Sync public roadmap` notifica o repositório do site para
validar e publicar a cópia estática.

## Configuração inicial

O workflow do site deve estar presente na branch padrão de `vemryx-site`. Crie
um fine-grained personal access token limitado somente a `marquezinii/vemryx-site`,
com a permissão de repositório **Contents: write**, e salve-o no repositório
Ralven como `VEMRYX_SITE_DISPATCH_TOKEN`. Esse é o único acesso entre os dois
repositórios; ele só pode disparar o evento de sincronização.

O site valida o JSON antes de fazer um commit automático somente quando o
conteúdo mudou. A integração Cloudflare–GitHub já conectada ao `main` publica
esse commit; não há token do Cloudflare no workflow do Roadmap.
