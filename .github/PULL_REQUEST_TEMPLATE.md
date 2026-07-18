## O que muda

<!-- Explique o problema, a decisão e o comportamento observável. -->

## Evidências

<!-- Links primários, benchmark reproduzível e distinção entre fato e inferência. -->

## Segurança e restauração

<!-- Pré-condições, caminhos afetados, privilégio, snapshot, validação e rollback. -->

## Validação

<!-- Comandos executados, resultado dos testes e ambiente. Inclua capturas para UI. -->

## Checklist

- [ ] Mantive o escopo em FiveM para GTAV Legacy e preservei o bloqueio de Enhanced.
- [ ] Não prometi ganho universal de FPS nem apresentei inferência como fato.
- [ ] Não desativei proteções, injetei código, alterei binários ou criei exclusões de antivírus.
- [ ] A mudança é tipada, limitada, idempotente e possui validação/rollback quando altera estado.
- [ ] Protegi `game-storage`, autenticação, configurações, plugins e dados pessoais.
- [ ] Adicionei ou atualizei testes proporcionais ao risco.
- [ ] Atualizei documentação e textos de interface afetados.
- [ ] Rodei build e testes em `Release` com o SDK de `global.json`.
- [ ] Não incluí binários, caches, dumps, segredos ou logs pessoais.
