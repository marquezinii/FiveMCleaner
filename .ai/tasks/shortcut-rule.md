# Regra de atalho na integração

- Agente: Codex
- Branch: `ai/codex/shortcut-rule`
- Status: concluída e pronta para integração
- Objetivo: tornar explícita a reconstrução do atalho de desenvolvimento antes de todo push integrador para `origin/dev/proxima-versao`.

## Alteração

- `AI_RULES.md` passa a tratar um pedido de integração em `origin/dev/proxima-versao` como autorização para enviar somente essa branch, depois de integrar, testar e reconstruir o atalho de desenvolvimento.
- A regra determina que o atalho reflita o estado final de `dev/proxima-versao`, nunca `main`, uma branch `ai/*` ou a instalação pública.

## Validação

- Revisão do trecho de integração e `git diff --check`.
