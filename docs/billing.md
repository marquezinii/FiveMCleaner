# Cobrança e acesso pago

## Estado atual

O repositório contém a **fundação de cobrança**, ainda sem checkout público e
sem concessão automática do plano Pro. Nenhuma versão distribuída cobra o
usuário enquanto preço, cancelamento, reembolso e confirmação do primeiro
pagamento não estiverem fechados e testados no ambiente de teste do provedor.

O aplicativo já consulta `GET /account/entitlements` após o login e permite
atualizar o status em Configurações, mostrando Gratuito, Pro com validade ou
indisponível. Esse snapshot fica somente em memória e apresenta a disponibilidade
[dos recursos Ultra](ultra.md). Os serviços revalidam o Pro antes de cada nova
operação paga; falha de leitura nunca concede acesso. Isso não cria checkout.

O primeiro provedor previsto é o Mercado Pago, com valores em BRL. A modelagem
interna continua neutra o suficiente para que identidade de conta e autorização
de acesso não dependam de IDs ou e-mail do provedor.

## Fronteiras de confiança

- O aplicativo autentica `GET /account/entitlements` com ID token Firebase. O
  Worker valida o token e consulta somente o Firebase UID verificado.
- O cliente nunca decide se uma conta é Pro e nunca envia UID, preço ou estado
  de assinatura para serem aceitos como autoridade.
- `POST /billing/mercado-pago/webhook` exige `data.id`, `x-request-id` e
  `x-signature`. Depois de validar o HMAC, o Worker busca a assinatura em
  `GET /preapproval/{id}` usando um Access Token armazenado como secret.
- Referência externa, moeda e valor retornados pelo Mercado Pago devem coincidir
  com um checkout intent criado pelo servidor. O corpo do webhook não concede
  acesso e não é persistido.
- A fundação reconcilia checkout intent, evento e assinatura de forma
  idempotente, mas não altera `account_entitlements`. A liberação do Pro deverá
  depender de uma cobrança aprovada, não apenas de uma assinatura com estado
  `authorized`.
- Enquanto existir um checkout ou uma assinatura vinculada,
  `DELETE /account/profile` responde `409 billing-cancellation-required`. A
  conta não perde o vínculo local antes de existir um cancelamento confirmado
  no provedor.

O D1 guarda apenas identificadores opacos, valores em centavos, estados
normalizados e timestamps necessários à reconciliação. Não guarda token,
assinatura HMAC, corpo de webhook, URL de checkout, senha ou e-mail do pagador.

## Contratos disponíveis

| Rota | Contrato |
| --- | --- |
| `GET /account/entitlements` | Autenticada; retorna `free` quando não existe acesso vigente e nunca expõe IDs do provedor. |
| `POST /billing/mercado-pago/webhook` | Valida origem, refaz a leitura autoritativa no Mercado Pago e reconcilia o estado sem conceder Pro. |

As credenciais são exclusivamente secrets do Worker:

```powershell
wrangler secret put MERCADO_PAGO_ACCESS_TOKEN
wrangler secret put MERCADO_PAGO_WEBHOOK_SECRET
```

Credenciais de produção nunca pertencem a `.dev.vars`, `wrangler.toml`, logs,
testes ou commits. Em desenvolvimento, use somente credenciais e usuários de
teste em um `.dev.vars` ignorado pelo Git.

## Bloqueadores antes de ativar vendas

1. Definir oferta, preço em BRL, periodicidade, política de cancelamento,
   reembolso e eventual carência.
2. Criar o checkout intent no Worker e só então criar a assinatura no Mercado
   Pago com referência externa opaca e URL de notificação explícita.
3. Reconciliar o primeiro e os próximos pagamentos aprovados antes de emitir ou
   renovar `ralven_pro`; falha, estorno e cancelamento precisam reduzir acesso
   segundo a política definida.
4. Implementar o cancelamento externo e só então liberar a exclusão de conta;
   até lá, o Worker bloqueia a exclusão quando encontra um checkout ou uma
   assinatura local.
5. Validar a oferta Ultra em piloto, preservando as funções gratuitas e a
   transparência do plano. O gating de rotinas, acompanhamento e medições já
   está implementado; disposição a pagar e retenção ainda precisam ser medidas.
6. Executar testes completos com credenciais de teste, reenvio, evento fora de
   ordem, timeout após commit, cancelamento, estorno e indisponibilidade do
   provedor antes de aplicar a migration e configurar secrets em produção.

Migração, secrets e deploy remoto são operações separadas do desenvolvimento
desta fundação e não acontecem automaticamente.

## Referências do provedor

- [Consultar uma assinatura (`GET /preapproval/{id}`)](https://www.mercadopago.com.br/developers/pt/reference/online-payments/subscriptions/get-preapproval/get)
- [Validar Webhooks de assinaturas](https://www.mercadopago.com.br/developers/pt/docs/subscriptions/additional-content/your-integrations/notifications/webhooks)
- [Confirmar pagamentos autorizados](https://www.mercadopago.com.br/developers/pt/docs/subscriptions/integration-configuration/subscription-no-associated-plan/authorized-payments)
