# Objetivo da tarefa

- **Agente:** Codex.
- **Objetivo:** investigar e reduzir o custo do Ralven em segundo plano e melhorar a responsividade, preservando o monitor local de sessão FiveM.
- **Escopo:** ciclo de vida da janela/bandeja, coleta e apresentação de métricas, monitoramento passivo e trabalho recorrente demonstravelmente desnecessário. Sem mudanças em otimizações do Windows/jogo, autenticação, release ou branches concorrentes.
- **Critérios de conclusão:** evidência reproduzível antes/depois, regressões para suspensão/retomada e monitor de sessão, build Release e validações aplicáveis aprovados; commit e PR para integração.
- **Resultado entregue:** coleta pausada por foco/visibilidade/minimização, cancelamento e retomada sem sobreposição, GPU em lote e monitor preservado sem atualizações ocultas redundantes. Comparação contra `efa9c8e` e harness WPF reproduzível: seis fases aprovadas; aproximadamente 95% menos alocações nas 20 coletas do painel, sem redução comprovada de RAM residente. Build Release sem avisos, 1.420 testes, segurança e formatação aprovados; atalho de desenvolvimento reconstruído. Limites e evidências em `docs/app-performance.md`; FiveM real/PC de entrada não medidos.
