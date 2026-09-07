# Configurar assinaturas do Ralven no Asaas

O backend está preparado para checkout recorrente hospedado. Vendas continuam
desativadas até o código e a migration estarem publicados e o fluxo ser testado.

## Sandbox

Crie uma conta Sandbox em **Integrações**, gere uma chave de API de homologação e
use `ASAAS_ENVIRONMENT=sandbox`. Nunca teste com cartão real. Valide o checkout,
os eventos e o cancelamento antes de tocar produção.

## Produção

Em **Integrações → Chaves de API**, crie uma chave exclusiva para o Ralven. Ela é
mostrada uma vez e deve ir diretamente para o secret `ASAAS_ACCESS_TOKEN` do
Cloudflare Worker.

Em **Integrações → Webhooks**, adicione:

- nome: `Ralven Billing`;
- URL: `https://api.vemryx.com/billing/asaas/webhook`;
- envio: sequencial;
- token próprio de 32 a 255 caracteres, diferente da chave de API;
- eventos de Checkout: `CHECKOUT_CREATED`, `CHECKOUT_PAID`, `CHECKOUT_CANCELED`, `CHECKOUT_EXPIRED`;
- eventos de assinatura: `SUBSCRIPTION_CREATED`, `SUBSCRIPTION_UPDATED`, `SUBSCRIPTION_INACTIVATED`, `SUBSCRIPTION_DELETED`;
- eventos de cobrança: criação/atualização, confirmação/recebimento, recusa,
  atraso, exclusão/restauração, análise de risco, estornos e chargebacks.

O mesmo token deve ser salvo no secret `ASAAS_WEBHOOK_TOKEN`. Não use a chave de
API como token do webhook.

## Ativação

1. Publique o Worker e aplique as migrations até `0009`.
2. Cadastre os dois secrets sem imprimi-los.
3. Mantenha `ASAAS_BILLING_ENABLED=false`.
4. Teste `GET /billing/return` e uma entrega simulada no log do webhook.
5. Execute um checkout controlado e confira que o Pro só aparece após a cobrança
   canônica estar `CONFIRMED` ou `RECEIVED`.
6. Teste cancelamento e um estorno controlado.
7. Só então altere a flag para `true` e faça um novo deploy.

Monitore **Logs de Webhooks**. Reenvios são normais e deduplicados pelo ID do
evento. Se a criação do checkout terminar em estado incerto, não tente novamente;
o registro precisa de reconciliação operacional.

Referências: [chaves de API](https://docs.asaas.com/docs/chaves-de-api),
[webhook pela aplicação](https://docs.asaas.com/docs/criar-novo-webhook-pela-aplicacao-web),
[idempotência](https://docs.asaas.com/docs/como-implementar-idempotencia-em-webhooks) e
[Sandbox](https://docs.asaas.com/docs/sandbox).
