# Objetivo da tarefa

- **Agente:** Codex
- **Objetivo:** manter a sessão autenticada do painel administrativo por 30 dias, sem armazenar a senha no navegador.
- **Escopo:** ajustar a validade da sessão administrativa persistida, a respectiva regressão e a documentação de autenticação. Não inclui mudar o provedor de autenticação, a senha, o modelo de autorização ou publicar a infraestrutura.
- **Critérios de conclusão:** um login cria uma sessão revogável de 30 dias; logout, revogação no servidor ou limpeza dos dados do site encerram o acesso; os testes do Worker passam.
- **Resultado entregue:** a sessão administrativa agora expira 30 dias após o login, mantendo cookie opaco, `HttpOnly`, `Secure`, revogação no servidor e encerramento por logout ou limpeza dos dados do site. Regressão e documentação atualizadas; 232 testes de lógica do Worker e 54 testes do dashboard aprovados.
