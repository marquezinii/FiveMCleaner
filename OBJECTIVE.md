# Reconciliação limitada do histórico de cobrança

- **Agente:** Codex.
- **Objetivo:** impedir que a leitura da página de billing refaça chamadas à Asaas para todo o histórico de uma assinatura, preservando recuperação de estornos e chargebacks tardios.
- **Escopo:** reconciliação Asaas no Worker, teste de regressão com muitos pagamentos mensais e documentação do comportamento. Não inclui deploy, migration, alteração de preço ou mudança no contrato público das rotas.
- **Critérios de conclusão:** custo de chamadas externas limitado por requisição; pagamentos recentes não relidos em atualizações consecutivas; webhook continua processando qualquer pagamento; ao menos um pagamento histórico elegível continua sendo auditado; chargeback tardio altera o ledger; testes Worker e verificações aplicáveis aprovados.
- **Resultado entregue:** reconciliação limitada a 180 dias e dez pagamentos por requisição, com intervalo de 15 minutos para itens recentes e auditoria rotativa de um item histórico após 30 dias. A deduplicação sintética agora reconhece mudanças semânticas de estorno/chargeback. Testes focados (13) e suíte Worker completa (247) aprovados; `npm audit` sem vulnerabilidades. Nenhum deploy ou migration foi realizado.
