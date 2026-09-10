# Espaço pessoal Pro: rotinas do Windows

## Escopo implementado

Espaço pessoal é a experiência do Ralven Pro para gerenciamento e otimização do Windows.
O nome interno Ultra permanece nos contratos e arquivos; não é um terceiro plano.
Leve, Médio e Agressivo continuam gratuitos, incluindo diagnóstico, prévia,
histórico, comparação básica e rollback.
O acesso à assinatura e à contratação está descrito em [billing.md](billing.md).

O valor proposto é montar uma recomendação conforme uso, pressão de recursos e
software de transmissão detectado, preservar preferências entre usos do PC,
acompanhar mudanças locais e repetir uma medição comparável. Ultra não aumenta o
limite de risco das ações existentes e não promete ganhos universais de desempenho.

## Rotinas e preferências

Há quatro rotinas salvas por perfil de usuário do Windows:

| Rotina | Uso atendido | Composição inicial |
| --- | --- | --- |
| Dia a dia | Navegador, comunicação e uso doméstico | Preserva aparência, desativa captura contínua quando nenhum software de transmissão é detectado e inclui responsividade de menus. |
| Jogos | Jogadores e entusiastas | Inclui Modo de Jogo, energia de desempenho na tomada e resposta consistente do ponteiro; preserva captura quando detecta software de transmissão. |
| Transmissão e gravação | Streamers e criadores | Inclui Modo de Jogo e energia de desempenho; preserva a gravação histórica do Windows. Não encerra aplicativos. |
| Trabalho e estudo | Estudantes, profissionais e uso individual em empresas | Mantém Modo de Jogo como está, preserva aparência e desativa captura contínua quando não foi detectada uma ferramenta de transmissão. |

O plano inteligente usa o diagnóstico atual. Pressão alta de recursos recomenda
reduzir efeitos visuais; software de transmissão conhecido preserva a captura em
segundo plano. A recomendação nunca inclui limpeza e fica visível para revisão
antes da execução.

Cada rotina pode salvar cinco preferências: preservar aparência, preservar
gravação histórica do Windows, permitir plano de energia de desempenho quando
conectado à tomada, limpar temporários com pelo menos 30 dias e usar resposta
consistente do ponteiro. Salvar não altera o Windows. Aplicar continua exigindo
prévia, confirmação e as condições nativas da ação.

A resposta consistente zera somente os dois limiares e o nível de aceleração por
`SPI_SETMOUSE`; a velocidade do ponteiro não é alterada. A ação captura e verifica
o estado anterior para rollback e não promete efeito em jogos que usam Raw Input.

“Preservar” mantém o estado atual; não desfaz uma otimização anterior. A
restauração continua no Histórico. Limpeza é opt-in e não pode ser desfeita.
Notebooks usam o guard de energia AC já existente. ASPM fica excluído porque a
ação disponível também altera parâmetros de bateria.

## Acompanhamento local

O usuário ativa explicitamente as leituras. O app guarda uma referência e
compara cada nova observação com a leitura anterior. Verifica Modo de Jogo,
gravação histórica, aceleração do ponteiro e transição para menos de 10 GiB livres na unidade do sistema.
Identidade do hardware e versão do Windows são renovadas pelo diagnóstico.

Há leitura após diagnóstico, após otimização e a cada 15 minutos com o app
aberto, se houver Pro vigente e nenhuma operação incompatível. Estados
indisponíveis não são tratados como configuração alterada. Não há serviço em
segundo plano, reaplicação automática ou correção automática.

## Medições guiadas

Cada coleta reúne 30 amostras de CPU, GPU, RAM e atividade de disco, usando os
leitores nativos existentes e intervalo de um segundo. Progresso conta amostras
reais. Cancelamento descarta a coleta incompleta.

Uma métrica só é publicada com ao menos 80% de amostras válidas. A comparação
requer mesma rotina, nome de tarefa, hardware e versão do Windows, 30 amostras e
duração entre 29 e 45 segundos nas duas coletas. Pelo menos uma métrica deve
estar disponível em ambas. O usuário precisa repetir a mesma atividade.

O usuário pode escolher qualquer medição retida e uma referência anterior.
O aplicativo sugere a última referência compatível; seleção da mesma medição,
referência posterior ou atividade incompatível não produz uma diferença.
Cards de CPU, GPU, memória e disco mostram valores e indisponibilidade explícita.
O resultado mostra utilização média e diferença em pontos percentuais.
Utilização menor não prova maior desempenho; não se calcula ganho de FPS,
latência ou estabilidade com essas leituras.

## Persistência, acesso e reversão

`%LOCALAPPDATA%/Ralven/Personal/workspace.json` contém até quatro rotinas,
60 eventos de mudança e 30 medições, com limite de 512 KiB. A interface apresenta
todas as 60 mudanças e 30 medições retidas. Os nomes das tarefas são locais,
com até 80 caracteres; não devem conter segredos. Os dados pertencem ao perfil
do Windows e não são sincronizados entre contas ou computadores.

Escritas usam arquivo temporário e substituição. Schema inválido, excesso de
tamanho ou reparse points bloqueiam a operação e preservam os dados existentes.
Nenhum dado novo é incluído na telemetria ou anexado automaticamente a relatos.

O snapshot de entitlement controla a apresentação. Antes de salvar, acompanhar,
medir ou executar Ultra, os serviços consultam novamente a autorização por ID
token Firebase. Troca de conta durante a consulta invalida o resultado.
Indisponibilidade ou expiração não concede Pro. O demo usa execução simulada
e armazenamento em memória.

Uma autorização válida inicia uma operação finita. Expiração não interrompe
transações em andamento nem apaga uma medição concluída. Leitura de registros,
exportação local de histórico em texto, pausa do acompanhamento e rollback
permanecem disponíveis sem assinatura. A exportação é explícita, reaproveita o
sanitizador de paths pessoais e exclui assinaturas de hardware, identidade de conta
e observações brutas. Como nomes de tarefa são texto livre local, a interface
orienta revisar o arquivo antes de compartilhar.

A introdução orienta a primeira rotina e oferece acesso direto ao Pro. O resumo
de mudanças mostra o último evento e permite solicitar nova análise ou revisar
o plano pessoal; nenhuma dessas ações reaplica ajustes automaticamente.
As políticas de cancelamento e cobrança continuam em [billing.md](billing.md).

`PersonalOptimizationPolicy` compõe exclusivamente opções suportadas.
`PersonalPreferences` é opcional no plano, restrito a `GeneralWindows` e ao
conjunto de ações disponível em `Aggressive`. Não existe um quarto valor no enum
persistido de perfis. Runtime e broker recompõem e comparam as opções canônicas.
Journals e relatórios registram `PersonalUsage` para identificar Ultra no
histórico; o campo fica ausente em transações comuns e antigas.

## Validação de produto antes das vendas

A utilidade e a disposição a pagar são hipóteses a validar em um piloto com
participantes das quatro rotinas, incluindo notebooks. Esta etapa não demonstra
retenção nem conversão comercial.

O piloto deve observar conclusão da primeira rotina, retorno para reutilizá-la,
uso de acompanhamento/comparação e motivos de abandono. Coletar feedback
voluntário sobre tempo poupado, clareza e confiança, sem ampliar silenciosamente
a telemetria. Não usar número de ajustes como medida de valor.

Preço e periodicidade dependem desse aprendizado e da conclusão do fluxo de
pagamento, renovação e cancelamento em ambiente de teste. Gestão de múltiplas
máquinas, políticas corporativas e integração com OBS não fazem parte desta
entrega.
