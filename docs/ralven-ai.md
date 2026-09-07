# Ralven AI

## Escopo da V1

O Ralven AI oferece orientação contextual para uma conta com entitlement
`ralven_pro`. Ele pode explicar o diagnóstico e recomendar somente um dos
perfis padrão `Light`, `Balanced` ou `Aggressive`.

A recomendação não é um plano executável produzido pelo modelo. O botão
**Revisar plano** seleciona o perfil no planejador local existente; preview,
confirmação, execução tipada, verificação e rollback continuam sob controle do
código C# e do broker allowlisted.

Ficam fora da V1: shell ou ferramentas do modelo, ações arbitrárias, execução
automática, memória persistente de conversa, streaming, roteamento entre
modelos e novos ajustes de Windows/FiveM.

## Fluxo e fronteiras

1. O app conclui o diagnóstico local e monta um objeto fechado com CPU, GPU,
   memória, processadores lógicos, espaço livre, Windows, arquitetura, score,
   pressão e os planos padrão gerados pelo catálogo atual.
2. Paths conhecidos na pergunta e no histórico curto são sanitizados no
   cliente. Paths do diagnóstico, arquivos, credenciais, tokens e dados da
   conta não fazem parte do contrato.
3. `POST /ai/message` verifica o Firebase ID Token, e-mail verificado,
   entitlement Pro, rate limit por UID, schema do payload e orçamento mensal.
4. O Worker chama a Responses API com `store: false`, reasoning `low`, sem
   tools e com saída JSON estrita (`answer` + `recommendedProfile`).
5. A saída é validada e bloqueada quando contém URL, bloco de código, comando
   de sistema ou orientação explícita para enfraquecer segurança.

Pergunta, conversa e snapshot não são persistidos no D1. `ralven_ai_usage`
guarda somente UID interno, período, estado, reserva/custo e contagens de
tokens para aplicar os tetos de custo. A chamada ao provedor inclui um
identificador de segurança pseudônimo e estável derivado do UID; e-mail, nome e
o token Firebase não são enviados ao provedor.

## Configuração operacional

O segredo `OPENAI_API_KEY` deve existir apenas no Cloudflare Worker. Modelo,
preços de cálculo, reserva e tetos mensais ficam em `wrangler.toml`; ausência
ou valor inválido bloqueia a rota. Os valores atuais usam `gpt-5.6-luna` e
devem ser revistos antes de cada mudança de modelo/preço.

Antes de ativar a rota em um ambiente remoto:

1. aplicar a migration `0010_ralven_ai_usage.sql`;
2. configurar `OPENAI_API_KEY` como secret do Worker;
3. revisar os tetos mensais e o rate limit;
4. executar os testes do Worker e um smoke test com uma conta Pro real;
5. publicar o Worker somente pelo fluxo autorizado de release/deploy.

Referências oficiais usadas para o contrato atual:

- [GPT-5.6 Luna](https://developers.openai.com/api/docs/models/gpt-5.6-luna)
- [Responses API](https://developers.openai.com/api/reference/cli/resources/responses/methods/create)
