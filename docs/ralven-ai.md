# Ralven AI

## Escopo da V1

O Ralven AI oferece orientação contextual para uma conta com os entitlements
ativos `ralven_pro` e `ralven_ai`. Pagamentos Pro canônicos concedem os dois no
mesmo período; um grant Pro isolado não concede IA por inferência. Ele pode
explicar o diagnóstico e recomendar somente um dos perfis padrão `Light`,
`Balanced` ou `Aggressive`.

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
3. `POST /ai/message` verifica o Firebase ID Token, e-mail verificado, os dois
   entitlements, rate limit por UID, schema fechado e orçamento antes de chamar
   o provedor.
4. O Worker chama a Responses API com `store: false`, reasoning `low`, sem
   tools, verbosidade baixa e saída JSON estrita (`answer` +
   `recommendedProfile`).
5. A saída é validada e bloqueada quando contém URL, bloco de código, comando
   de sistema ou orientação explícita para enfraquecer segurança.
6. O cliente repete no máximo uma falha de transporte com o mesmo UUID. O
   Worker deriva dele um ID HMAC por conta e impede uma segunda chamada paga.

Pergunta, conversa e snapshot não são persistidos no D1. `ralven_ai_usage`
guarda somente UID interno, período, estado, reserva/custo e contagens de
tokens de entrada normal, cache, escrita de cache, saída e raciocínio. O
identificador de segurança enviado ao provedor é um HMAC do UID com domínio
próprio; e-mail, nome, UUID do cliente e token Firebase não são enviados.

O orçamento do usuário é indexado pelo mês em que começa seu período pago; o
teto global usa o mês UTC real da chamada. A reserva é feita antes do provedor e
liquidada pelo uso reportado. Se uma falha de transporte tornar o custo
incerto, a reserva permanece contabilizada em vez de liberar limite que pode já
ter sido consumido.

## Configuração operacional

Os segredos `OPENAI_API_KEY` e `RALVEN_AI_SAFETY_IDENTIFIER_SECRET` devem existir
apenas no Cloudflare Worker e ser distintos. Modelo, preços, reserva e tetos
ficam em `wrangler.toml`; ausência ou valor inconsistente bloqueia a rota. A
chave do provedor não basta para ativá-la: `RALVEN_AI_ENABLED` permanece
`false` até uma ativação explícita. Os valores atuais usam `gpt-5.6-luna` e
devem ser revistos antes de cada mudança de modelo ou preço.

Antes de ativar a rota em um ambiente remoto:

1. aplicar as migrations até `0011_ralven_ai_foundation.sql`;
2. configurar os dois secrets do Worker sem gravá-los no repositório;
3. revisar modelo, preços, reserva, tetos mensais e rate limit;
4. manter `RALVEN_AI_ENABLED=false` durante a validação de configuração;
5. executar testes do Worker e um smoke controlado com conta que possua os dois
   entitlements;
6. habilitar a flag e publicar somente pelo fluxo autorizado de release/deploy.

## Estado antes da credencial real

| Estado | Entrega |
| --- | --- |
| Implementado | UI e contexto local sanitizado; autenticação; acesso Pro + IA separado; rate limit; schema fechado; orçamento/reserva; custo por categoria de token; idempotência; HMAC; resposta estruturada; `store: false`; revisão no planejador local. |
| Operacional pendente | migrations remotas, dois secrets, revisão final dos preços/tetos, ativação explícita, deploy e smoke real. |
| Deliberadamente adiado | tools mesmo read-only, streaming, memória persistente, retry de provedor, créditos, painel administrativo de IA, attestation de dispositivo e roteamento de modelos. |

Os itens adiados não são necessários para a V1 contextual e ampliariam custo ou
superfície de ataque antes de existir evidência de necessidade.

Referências oficiais usadas para o contrato atual:

- [GPT-5.6 Luna](https://developers.openai.com/api/docs/models/gpt-5.6-luna)
- [Responses API](https://developers.openai.com/api/reference/cli/resources/responses/methods/create)
