# Objective

- **Agente:** Codex
- **Objetivo:** Publicar o painel administrativo integrado na `dev/proxima-versao` sem depender de uma release do aplicativo e retirar os endereços legados.
- **Escopo:** Automação de deploy do Worker/painel, configuração de origem e documentação do dashboard; exclusão do projeto Cloudflare Pages legado após validação.
- **Fora do escopo:** Release do aplicativo Windows, alterações de dados D1 e mudanças de contratos de telemetria.
- **Critérios de conclusão:** O Pages canônico serve o commit integrado, aponta somente para `api.vemryx.com`, e o projeto Pages legado deixa de resolver.
- **Resultado entregue:** O dashboard integrado na `dev/proxima-versao` foi publicado em `dashboard.vemryx.com`; o projeto Pages `ralven-dashboard` foi excluído e o subdomínio técnico remanescente redireciona permanentemente ao domínio canônico. A automação passa a publicar mudanças de Worker e dashboard após a CI da `dev`.
