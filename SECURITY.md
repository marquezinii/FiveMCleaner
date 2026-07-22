# Política de segurança

O FiveMCleaner altera configurações locais e possui operações que podem exigir elevação. Uma falha nesse limite de confiança é tratada com prioridade.

## Versões cobertas

Enquanto o projeto estiver na série `0.x`, recebem correções:

| Versão               | Suporte |
| -------------------- | ------- |
| branch `main`        | sim     |
| release mais recente | sim     |
| releases anteriores  | não     |

Atualize para a versão mais recente antes de relatar um problema já corrigido.

## Como relatar uma vulnerabilidade

Não abra uma issue pública, não publique prova de conceito e não anexe logs com dados pessoais.

O formulário **Relatar um bug** do aplicativo usa o serviço externo FormSubmit e é destinado apenas a bugs comuns. Não envie por ele vulnerabilidades, credenciais, provas de conceito, dumps ou outros dados sensíveis. O fluxo e os dados processados estão descritos em [docs/bug-reports.md](docs/bug-reports.md).

1. Abra a aba **Security** do repositório.
2. Escolha **Report a vulnerability** para iniciar um relato privado.
3. Informe versão ou commit, impacto, pré-condições, passos mínimos para reprodução e uma sugestão de correção, se houver.
4. Remova nomes de usuário, tokens, caminhos pessoais, endereços de servidor e outros dados identificáveis dos anexos.

Se o botão de relato privado não estiver disponível, abra uma issue sem detalhes técnicos pedindo um canal privado aos mantenedores. Não inclua o conteúdo sensível nessa issue.

Metas de atendimento, e não garantias contratuais:

- confirmação inicial em até 72 horas;
- triagem e classificação em até 7 dias;
- atualização de andamento ao menos a cada 14 dias enquanto a correção estiver em curso.

O projeto solicita divulgação coordenada. Crédito será oferecido quando desejado e quando o relato for original e acionável.

## Escopo prioritário

Exemplos de vulnerabilidades relevantes:

- execução de comando ou caminho arbitrário pelo broker elevado;
- escape da lista permitida de operações administrativas;
- exclusão fora dos diretórios previamente validados;
- traversal, reparse point, junction ou symlink capaz de redirecionar uma ação;
- restauração que grava em destino diferente do snapshot;
- adulteração de plano entre a prévia e a aplicação;
- exposição de credenciais ou dados pessoais em logs;
- download ou execução de conteúdo sem autenticação e validação.

Variação de FPS, incompatibilidade de hardware, consumo normal de cache e uma detecção antivírus isolada não são, por si só, vulnerabilidades. Ainda assim, falsos positivos reproduzíveis podem ser relatados como bug com o nome do produto, versão das assinaturas e hash do binário — nunca envie arquivos pessoais a serviços externos sem autorização.

## Princípios do projeto

- A interface roda sem privilégio administrativo permanente.
- Elevação ocorre somente para ações tipadas, conhecidas e apresentadas na prévia.
- O broker não aceita shell, script ou linha de comando arbitrária.
- Caminhos são canonicalizados e revalidados imediatamente antes da mutação.
- Configurações pequenas recebem snapshot e rollback verificável.
- O app não desativa antivírus, firewall, UAC ou Windows Update.
- O app não injeta DLL, altera memória, patcha executáveis nem cria exclusões de antivírus.
- GTAV Enhanced permanece bloqueado até existir integração específica e auditada.

Consulte também [docs/safety.md](docs/safety.md) e [docs/architecture.md](docs/architecture.md).
