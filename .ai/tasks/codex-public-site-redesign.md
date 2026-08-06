# Redesign da central pública de download

- Agente: Codex
- Branch: `ai/codex/public-site-redesign`
- Objetivo: retrabalhar integralmente a página pública de download com direção visual premium, profundidade 3D e animações acessíveis.
- Status: integrado e validado em 02/08/2026.

## Mudanças

- Reconstruída `website/public-site` com hero tridimensional, prévia do produto em perspectiva, composição bento, release, segurança e CTA responsivos.
- Adicionadas animações de entrada, ambiente, progresso, parallax por ponteiro e barra de rolagem, com fallback sem JavaScript e respeito a `prefers-reduced-motion`.
- Preservados os links oficiais de download/GitHub, a versão pública `1.2.0`, os requisitos e os limites reais de privacidade e segurança.
- Criado `og.png` próprio (1200×630) e metadados Open Graph/Twitter.
- Consolidado o CSS em `styles.css`; removido o complemento legado `site-polish.css`.
- Atualizado o teste de contrato da página pública para cobrir 3D, JavaScript, movimento reduzido e card social.

## Validação

- `npm.cmd ci`: aprovado; 509 pacotes auditados, 0 vulnerabilidades.
- `npm.cmd run lint`: aprovado.
- `npm.cmd test`: aprovado; build Vinext e 3/3 testes.
- `git diff --check`: aprovado.
- Card social validado com 1200×630 px.

## Observações

- Nenhuma dependência foi adicionada.
- Não houve alteração de versão, release, push, deploy ou publicação do GitHub Pages.
- Commit: preenchido pelo próprio commit desta tarefa.
