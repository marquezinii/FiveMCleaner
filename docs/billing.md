# Cobrança e acesso pago

O Ralven Pro custa **R$ 19,90 por mês** e usa o checkout recorrente hospedado do
Asaas. O cartão e os dados cadastrais são informados na página do Asaas e nunca
passam pelo aplicativo. Diagnóstico, perfis padrão, histórico básico e rollback
continuam Free; segurança e restauração nunca dependem de assinatura.

Vendas permanecem desativadas por padrão. Código e testes locais não substituem
homologação no Sandbox, migrations remotas, webhook público e um teste completo
antes de habilitar produção.

## Contratos

Todas as rotas de conta usam o Firebase UID validado no Worker. O cliente não
define preço, periodicidade, provedor, IDs nem estado de pagamento.

| Rota | Contrato |
| --- | --- |
| `GET /billing/return` | Página informativa; não concede acesso. |
| `GET /account/billing` | Oferta, disponibilidade e estado normalizado da assinatura. |
| `POST /account/billing/checkout` | Recebe somente `{ offerKey }` e devolve a URL hospedada. |
| `POST /account/billing/cancel` | Recebe `{}` e interrompe a recorrência no Asaas. |
| `GET /account/entitlements` | Retorna o acesso server-authoritative da conta. |
| `POST /billing/asaas/webhook` | Autentica, deduplica e reconcilia eventos do Asaas. |

A chave da oferta inclui o preço, como `ralven_pro_monthly_1990`. Um consentimento
antigo não aceita silenciosamente um preço novo. Há no máximo um checkout aberto
por conta.

## Checkout e reconciliação

O Worker cria `POST /v3/checkouts` com `CREDIT_CARD`, `RECURRENT`, ciclo `MONTHLY`,
expiração de 60 minutos e uma referência interna aleatória. A URL aceita pelo
desktop precisa ser exatamente
`https://asaas.com/checkoutSession/show?id=<id-opaco>`.

A API de Checkout não documenta uma chave de idempotência para criação. Por isso,
o Worker registra a tentativa antes da chamada e nunca recria automaticamente
quando há timeout sem ID confirmado. O estado exige reconciliação operacional,
evitando assinatura ou cobrança duplicada.

O retorno do navegador não prova pagamento. O Pro só é concedido depois que o
Worker consulta `GET /v3/payments/{id}` e `GET /v3/subscriptions/{id}` e confirma:

- cobrança de cartão, valor de R$ 19,90 e assinatura mensal;
- vínculo ao checkout opaco da conta ou a uma assinatura já vinculada;
- estado `CONFIRMED` ou `RECEIVED`;
- ausência de estorno concluído ou chargeback.

O ledger usa o ID único da cobrança. O período inicia na data canônica de
confirmação/pagamento e termina um mês depois, preservando o último dia possível.
Eventos repetidos não somam validade. Qualquer estorno `DONE`, inclusive parcial,
ou chargeback retira o acesso daquele período. Uma atualização manual reconcilia
cobranças do checkout e recupera webhook perdido sem polling contínuo.

O webhook valida `asaas-access-token` em tempo constante antes de ler o JSON.
O `id` do evento garante idempotência; o corpo serve para localizar o recurso e
campos novos são ignorados. Dados usados para conceder acesso são relidos da API.
Eventos de exclusão só podem reduzir acesso. Falhas temporárias retornam erro para
o Asaas repetir a entrega.

## Cancelamento e exclusão

Checkout pendente é cancelado em `POST /v3/checkouts/{id}/cancel`. Assinatura
existente é encerrada em `DELETE /v3/subscriptions/{id}`. O período já pago segue
válido até sua data final e o cancelamento não faz reembolso. Exclusão de conta
fica bloqueada enquanto houver vínculo de cobrança ainda aberto.

## Configuração

Secrets exclusivos do Worker:

```powershell
wrangler secret put ASAAS_ACCESS_TOKEN
wrangler secret put ASAAS_WEBHOOK_TOKEN
```

Variáveis não secretas:

```text
ASAAS_BILLING_ENABLED=false
ASAAS_ENVIRONMENT=production
ASAAS_AMOUNT_CENTS=1990
ASAAS_RETURN_URL=https://api.vemryx.com/billing/return
```

`ASAAS_ENVIRONMENT` aceita somente `sandbox` ou `production`, e o prefixo da
chave precisa corresponder ao ambiente. `ASAAS_WEBHOOK_TOKEN` tem de ser distinto
da chave de API, sem espaços e com 32 a 255 caracteres. Ausência ou inconsistência
mantém o checkout desativado.

Aplicar as migrations até `0009` junto ao código. Se uma base antiga tiver mais
de um checkout aberto para a mesma conta, investigue no provedor antes de migrar.
Credenciais nunca pertencem a `.dev.vars`, logs, testes, commits ou ao desktop.

## Homologação

Antes de habilitar vendas, validar no Sandbox: aprovação e recusa, renovação,
webhooks repetidos e fora de ordem, retorno visual, timeout de criação, pagamento
perdido recuperado por atualização, cancelamento, estorno parcial/total,
chargeback e indisponibilidade do provedor. Depois repetir um fluxo controlado em
produção e publicar preço, recorrência, cancelamento, reembolso e atendimento.

Consulte [Configuração Asaas](asaas-setup.md) para o procedimento operacional.

## Referências oficiais

- [Autenticação](https://docs.asaas.com/docs/autenticacao-1)
- [Checkout recorrente](https://docs.asaas.com/docs/checkout-com-assinatura-recorrente)
- [Lista de cobranças e filtro por checkout](https://docs.asaas.com/reference/listar-cobrancas)
- [Eventos de cobrança](https://docs.asaas.com/docs/webhook-para-cobrancas)
- [Eventos de assinatura](https://docs.asaas.com/docs/eventos-para-assinaturas)
- [Eventos de Checkout](https://docs.asaas.com/docs/eventos-para-checkout)
- [Estornos](https://docs.asaas.com/docs/estornos)
- [Remover assinatura](https://docs.asaas.com/reference/remove-subscription)
