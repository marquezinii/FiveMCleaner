# Configurar assinaturas do Ralven no Mercado Pago

Você pode deixar esta etapa para depois. **As vendas estão desativadas por padrão.**
O desenvolvimento local não criou cobranças, configurou sua conta Mercado Pago
nem publicou o backend de pagamentos. Este guia orienta a ativação futura.

O cliente escolhe Pro no Ralven e paga no site do Mercado Pago. O Ralven recebe
a confirmação pelo servidor e libera o período pago. Dados de cartão não passam
pelo aplicativo. Voltar do checkout, sozinho, não libera Pro.

## 1. O que você fará na sua conta

1. Entre no [Mercado Pago Developers](https://www.mercadopago.com.br/developers/pt)
   com a conta que receberá os pagamentos.
2. Abra **Suas integrações → Criar aplicação** e dê um nome identificável,
   como `Ralven`. Complete a verificação de identidade, se solicitada.
3. Informe pagamentos online e desenvolvimento próprio quando o formulário
   pedir. A integração do Ralven é **Assinaturas via API, com checkout hospedado**.
   Não é necessário criar um plano de assinatura manual nem um link público:
   o servidor cria a assinatura individual quando o cliente inicia o checkout.
4. Guarde o nome/ID da aplicação para identificar a configuração posteriormente.
   Se o formulário oferecer opções diferentes, confira a documentação ou retome
   a configuração assistida antes de escolher outro produto.

Esses passos foram consultados na documentação; o painel privado da sua conta
não foi validado durante o desenvolvimento. [Criação de aplicação](https://www.mercadopago.com.br/ajuda/20152),
[assinatura com checkout hospedado](https://www.mercadopago.com.br/developers/pt/docs/subscriptions/integration-configuration/subscription-no-associated-plan/pending-payments).

## 2. Primeiro, testar sem dinheiro real

Na aplicação, abra **Contas de teste**. Use um vendedor e um comprador de teste,
ambos do Brasil; o painel pode já ter criado contas automaticamente. Use os
dados e cartões de teste fornecidos pelo Mercado Pago, nunca um cartão real.

As credenciais usadas devem corresponder ao vendedor de teste e à integração
selecionada. **Não identifique teste/produção só pelo prefixo do Access Token**:
confira a conta e a seção do painel. Não misture comprador de teste com vendedor
real. O operador pode conferir essa combinação com você antes da homologação.
[Contas de teste](https://www.mercadopago.com.br/developers/pt/docs/subscriptions/additional-content/your-integrations/test/accounts),
[compra de teste](https://www.mercadopago.com.br/developers/pt/docs/subscriptions/integration-test/payment-approval).

## 3. Conectar o Mercado Pago ao servidor

O operador/Codex poderá preparar um ambiente de teste isolado, aplicar as
migrations necessárias e informar a origem HTTPS exata do backend. Depois,
na aplicação Mercado Pago, abra **Webhooks → Configurar notificações**.

- URL de notificações: `https://<origem-do-backend>/billing/mercado-pago/webhook`.
- Ative **Planos e assinaturas** e **Pagamentos**, incluindo os tópicos
  `subscription_preapproval`, `subscription_authorized_payment` e `payment`.
- Salve e localize a assinatura secreta gerada para validar as notificações.
- A URL de retorno do checkout será `https://<origem-do-backend>/billing/return`.
  Ela é diferente da URL de notificações e orienta o cliente a voltar ao app.

Substitua o marcador pela origem confirmada pelo operador; não use o marcador
literalmente. [Configuração oficial de Webhooks](https://www.mercadopago.com.br/developers/pt/docs/subscriptions/additional-content/your-integrations/notifications/webhooks).

Você precisará de dois segredos: **Access Token** do vendedor e **assinatura
secreta do webhook**. Não envie nenhum deles no chat, em screenshots ou no Git.
O operador poderá abrir os prompts abaixo no diretório `infra/cloudflare-worker`,
apontados para o ambiente correto, para você inserir os valores diretamente:

```powershell
wrangler secret put MERCADO_PAGO_ACCESS_TOKEN
wrangler secret put MERCADO_PAGO_WEBHOOK_SECRET
```

Esses comandos alteram a configuração remota; são instruções para a etapa futura,
não foram executados por este guia. O desktop não precisa receber esses segredos.
As variáveis sem segredo serão configuradas no backend:

```text
MERCADO_PAGO_BILLING_ENABLED=false
MERCADO_PAGO_AMOUNT_CENTS=1990
MERCADO_PAGO_RETURN_URL=https://<origem-do-backend>/billing/return
```

`1990` significa R$ 19,90 por mês. A oferta é controlada pelo servidor.
Para testar o checkout, habilite a flag somente no ambiente isolado com contas
de teste; a configuração pública continua desativada até concluir a homologação.

## 4. Antes de começar a vender

- [ ] Completar checkout de teste e confirmar Pro somente após pagamento aprovado.
- [ ] Testar recusa, renovação, retorno ao app e atualização do plano.
- [ ] Reabrir checkout e simular timeout/reenvio: deve existir uma única assinatura.
- [ ] Testar notificações repetidas, atrasadas e recuperação de notificação perdida.
- [ ] Usar **Simular** no painel Webhooks para confirmar a entrega e a resposta do
  servidor; uma compra com credenciais de teste pode não enviar notificações.
- [ ] Cancelar e confirmar no provedor: futuras renovações param; período pago continua.
- [ ] Testar reembolso parcial/total e conferir a retirada do período correspondente.
- [ ] Publicar preço, condições, cancelamento, política de reembolso e atendimento.
- [ ] Com homologação concluída, configurar credenciais/URLs de produção e autorizar
  o deploy e a habilitação pública. Verificar o fluxo publicado antes de divulgá-lo.

Se uma criação ficar incerta, atualize o plano e peça reconciliação ao operador;
não apague vínculos nem crie assinaturas manualmente para tentar destravar.
Cancelar renovação não executa reembolso automático.

Detalhes técnicos, limites e testes estão em [Cobrança e acesso pago](billing.md).
