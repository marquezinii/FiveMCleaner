# Cobrança e acesso pago

## Oferta e estado de ativação

O Ralven Pro oferece rotinas Ultra, acompanhamento local opt-in, medições guiadas e organização pessoal. Diagnóstico, perfis padrão, histórico básico e rollback continuam gratuitos; segurança e restauração nunca dependem de assinatura. O preço inicial configurado é **R$ 19,90 por mês**, sem teste gratuito ou promessa de ganho universal de desempenho.

O código implementa checkout hospedado no Mercado Pago, confirmação de pagamento, acesso por período pago e cancelamento. **Vendas permanecem desativadas por padrão** em `wrangler.toml`. A implementação e seus testes locais não equivalem a uma homologação financeira: credenciais reais, homologação com contas de teste, configuração do provedor e deploy são etapas operacionais separadas.

## Contratos do aplicativo

Todas as rotas de conta usam o Firebase UID verificado no servidor. O preço, a periodicidade e a identidade do provedor nunca são aceitos como autoridade do cliente.

| Rota | Contrato |
| --- | --- |
| `GET /billing/return` | Página pública informativa, sem autenticação nem concessão de acesso. |
| `GET /account/billing` | Oferta `{ key, amountCents, currency, intervalMonths }`, `checkoutAvailable`, `billingUnavailableReason` e `subscription` ou `null`. |
| `POST /account/billing/checkout` | JSON `{ offerKey }`; retorna `{ checkoutUrl }`. Exige perfil e e-mail Firebase verificado. |
| `POST /account/billing/cancel` | JSON `{}`; confirma cancelamento externamente e retorna o snapshot de billing. |
| `GET /account/entitlements` | Contrato anterior preservado: `{ tier, entitlements, validUntil }`. |
| `POST /billing/mercado-pago/webhook` | Envelope HMAC e leitura autoritativa de assinatura/fatura/pagamento. |

A chave pública da oferta inclui o preço, por exemplo `ralven_pro_monthly_1990`. Consentimento sobre uma oferta antiga não cria uma assinatura com preço novo. Um checkout pendente mantém seu preço original. A oferta aceita entre 1 e 100.000 centavos, em BRL, com intervalo mensal; mudanças de preço afetam novos contratos, não mandatos existentes.

`subscription` contém `state` (`pending`, `authorized`, `paused`, `cancelled`), `accessUntil`, `canCancel` e `renewsAt`. Este último permanece `null`: data de cobrança futura não comprova período pago. Estado `authorized` informa o mandato de renovação, não concede Pro. `checkoutAvailable` permite iniciar ou retomar checkout pendente, mas não duplicar assinatura autorizada/pausada.

Erros são códigos genéricos, sem payload do provedor: `invalid-offer`, `billing-unavailable`, `provider-temporarily-unavailable`, `billing-checkout-in-progress`, `billing-checkout-reconciliation-required`, `billing-subscription-exists`, `billing-cancellation-unconfirmed` e `billing-rate-limited`. O cliente deve atualizar a oferta após `invalid-offer` e nunca repetir cegamente uma criação incerta. As mutações têm limite obrigatório de 10 pedidos por minuto por UID; a atualização de billing permite 20 consultas por minuto por UID. Uma assinatura cancelada com período ainda pago não pode criar nova cobrança antes da expiração.

## Checkout, concorrência e recuperação

O Worker persiste primeiro um intent com referência externa opaca e usa uma constraint SQLite para permitir somente um mandato aberto por conta. Uma reivindicação atômica permite uma única tentativa de criação. O e-mail Firebase verificado é enviado transitoriamente ao Mercado Pago como pagador; não é persistido nas tabelas de cobrança.

O checkout cria `POST /preapproval` com `status=pending`; os dados de cartão são preenchidos no site do Mercado Pago. Apenas `https://www.mercadopago.com.br/subscriptions/checkout` com o ID exato do mandato pode ser devolvido ao aplicativo. ID token Firebase nunca vai para o browser, query ou callback.

`X-Idempotency-Key` usa o intent, mas o sistema não presume garantia de idempotência para preapproval. Depois de falha incerta, busca mandatos pelo e-mail autenticado e compara a referência opaca exata. O e-mail é filtro de busca, nunca prova de propriedade. Busca incompleta, mais de um resultado para a referência ou ausência de confirmação produz `billing-checkout-reconciliation-required`, sem recriar cobrança. O limite operacional de recuperação é 500 resultados e dez páginas; ampliar a recuperação deve manter a propriedade por referência exata. Mudança do e-mail durante uma criação incerta pode exigir reconciliação operacional. Nunca limpe esses intents automaticamente por idade.

## Pagamentos e acesso

Assinatura autorizada e fatura `processed` não são comprovação de pagamento. Para `subscription_authorized_payment`, o Worker consulta `GET /authorized_payments/{id}`, encontra seu pagamento e relê `GET /v1/payments/{id}` e `GET /preapproval/{id}`. Confere a relação fatura/pagamento/assinatura/intent, valor integral, moeda e periodicidade.

Somente pagamento canônico `approved`, com data de aprovação válida e sem valor reembolsado, concede acesso. Cada fatura gera intervalo mensal a partir de `debit_date`, com ajuste para o último dia do mês. Reenvio não soma dias, não desloca a validade e não concede antes do começo do período. A leitura de entitlements recompõe o snapshot dos períodos registrados, inclusive a ativação de renovação paga antecipadamente.

A migration `0009_billing_checkout_payments.sql` acrescenta o ledger de pagamentos e a exclusividade de checkout. Pagamentos têm IDs únicos, estado normalizado e versão temporal do provedor. As gravações de pagamento, snapshot de acesso e processamento de evento são transacionais. Eventos antigos não substituem estado novo. Recusa, cancelamento do pagamento, disputa/chargeback ou qualquer valor reembolsado retira o acesso daquele período; outro período aprovado vigente continua válido. Essa política conservadora de reembolso precisa ser informada antes de vender.

Eventos `payment` usam o vínculo de fatura previamente confirmado ou consultam `GET /authorized_payments/search?payment_id=...` e exigem o ID exato do pagamento na fatura canônica. Tentativas repetidas da mesma fatura podem ter pagamentos diferentes; o ledger preserva cada pagamento. Um evento de pagamento antigo reconsulta aquele pagamento, mesmo se a fatura já aponta para uma tentativa mais nova. Associação ausente/ambígua retorna 503 para redelivery, sem inferir vínculo por e-mail ou pelo corpo do webhook. Atualizar o plano no aplicativo também reconcilia faturas por `preapproval_id`, recuperando notificações perdidas. A consulta tem limites de 500 faturas, dez páginas e quatro períodos relevantes por atualização; excesso exige reconciliação operacional. A operação deve monitorar falhas/reentregas; não declarar pagamento confirmado a partir do retorno visual do checkout. O browser retornar não concede acesso.

## Cancelamento, reembolso e exclusão

Cancelar interrompe futuras renovações e preserva o período já aprovado. O Worker envia cancelamento e faz uma leitura posterior; resposta HTTP de escrita sem estado canônico cancelado não libera exclusão. Mandato pendente também deve ser cancelado no provedor. Somente um intent comprovadamente sem tentativa de criação pode ser encerrado localmente.

A exclusão de conta fica bloqueada enquanto existir um vínculo não cancelado. Após a confirmação, a exclusão remove os dados locais de cobrança vinculados à conta; não equivale a reembolso. Reembolso não é automático no fluxo de cancelamento: deve ser solicitado pelo canal de atendimento/Mercado Pago, respeitando a política comercial e direitos aplicáveis. Reembolsos/chargebacks recebidos do provedor são reconciliados pelo ledger. Política pública, canal de atendimento e procedimentos financeiros devem estar publicados e homologados antes de habilitar vendas.

## Configuração operacional

Secrets exclusivos do Worker:

```powershell
wrangler secret put MERCADO_PAGO_ACCESS_TOKEN
wrangler secret put MERCADO_PAGO_WEBHOOK_SECRET
```

Variáveis não secretas:

```text
MERCADO_PAGO_BILLING_ENABLED=false
MERCADO_PAGO_AMOUNT_CENTS=1990
MERCADO_PAGO_RETURN_URL=https://fivemcleaner-telemetry.felipemarquesini10.workers.dev/billing/return
```

A rota `/billing/return` é fornecida pelo próprio Worker. A URL acima passa a existir após o deploy desta versão; não requer outro site. A página ignora todos os parâmetros, não recebe ID token e não afirma aprovação. Instrui voltar ao aplicativo, abrir Ralven Pro e usar Atualizar assinatura. Não carrega scripts, fontes ou serviços externos; aplica CSP com nonce de estilo, no-store, no-referrer e bloqueio de frames.

A flag só deve virar `true` após homologação. Ausência de token, segredo HMAC ou URL HTTPS válida desativa o checkout. Desativar novas vendas não desativa confirmação de pagamentos nem cancelamento de mandatos existentes.

Configure no painel da aplicação Mercado Pago a URL HTTPS `/billing/mercado-pago/webhook` e os tópicos `subscription_preapproval`, `subscription_authorized_payment` e `payment`. A API oficial de preapproval não garante `notification_url` por mandato; não dependa desse campo. Notificações usam o `data.id` da query e os headers `x-request-id` e `x-signature`. O tópico normalmente vem em `type` na query. Como a documentação de assinaturas também descreve `type` no JSON sem garantir essa query, existe fallback limitado a 8 KiB: somente após validar HMAC, aceita um tópico allowlisted cujo `body.data.id` coincide exatamente com o ID assinado. O corpo é apenas dica de roteamento; nunca decide pagamento ou acesso e não é persistido.

Aplicar as migrations até `0009` junto ao código consumidor. A constraint de checkout falha se uma base antiga contiver múltiplos intents abertos para uma conta: investigue cada vínculo no provedor antes de migrar; não apague ou escolha um arbitrariamente. Credenciais de produção nunca pertencem a `.dev.vars`, logs, testes ou commits. Desenvolvimento usa apenas contas de teste e credenciais de teste em arquivo ignorado.

## Homologação antes de vendas

Executar com contas de teste: checkout e retorno, primeira cobrança recusada/aprovada, renovação, reenvio, eventos invertidos, timeout após criação e após commit, cancelamento confirmado/indisponível, reembolso parcial/total, chargeback, exclusão e indisponibilidade do provedor. Conferir o cancelamento `cancelled` aceito pela conta/API e o formato real de todos os campos de fatura; as fontes do provedor usam grafias diferentes para cancelamento.

Os testes locais usam SQLite real, assinaturas HMAC de teste e respostas de provedor simuladas. Não validam credenciais, aceitação de cartão, configuração da conta Mercado Pago, entrega pública de notificações ou políticas comerciais. Nenhuma cobrança, migration remota ou deploy acontece automaticamente com os testes.

## Referências oficiais

- [Checkout pendente sem plano associado](https://www.mercadopago.com.br/developers/pt/docs/subscriptions/integration-configuration/subscription-no-associated-plan/pending-payments)
- [Consulta de assinatura](https://www.mercadopago.com.br/developers/pt/reference/online-payments/subscriptions/get-preapproval/get)
- [Webhooks](https://www.mercadopago.com.br/developers/pt/docs/subscriptions/additional-content/your-integrations/notifications/webhooks)
- [Busca de pagamentos autorizados](https://www.mercadopago.com.br/developers/pt/reference/online-payments/subscriptions/authorized-payment-search/get)
- [Pagamentos autorizados](https://www.mercadopago.com.br/developers/pt/docs/subscriptions/integration-configuration/subscription-no-associated-plan/authorized-payments)
