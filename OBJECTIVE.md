# Objetivo da tarefa

- **Agente:** Codex
- **Objetivo:** Atualizar as dependências compatíveis do site e manter o lint e a tipagem funcionais.
- **Escopo:** `website/package.json` e `website/package-lock.json`.
- **Fora de escopo:** Atualizar TypeScript para 7 ou ESLint para 10 antes de a cadeia do `eslint-config-next` oferecer suporte compatível.
- **Critérios de conclusão:** O site usa as versões compatíveis do Next, tipos, TypeScript e ESLint, e as validações do site passam no ambiente suportado.
- **Resultado entregue:** Atualizados Next e tipos; TypeScript 6 e ESLint 9 foram mantidos após a CI do Dependabot provar incompatibilidade com TypeScript 7 e ESLint 10.
