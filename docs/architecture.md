# Arquitetura

Este documento descreve a arquitetura-alvo e os limites entre componentes. Uma classe ou fluxo só deve ser tratado como entregue quando existir implementação e teste correspondente.

## Objetivos

- manter a interface sem privilégio administrativo permanente;
- representar cada alteração como ação pequena, tipada e reversível;
- separar descoberta Windows de política de produto;
- impedir que um perfil amplie silenciosamente o escopo de uma ação;
- oferecer progresso real por etapas, não uma animação temporal;
- suportar instalação personalizada do FiveM Legacy;
- bloquear GTAV Enhanced até existir adaptador próprio;
- permitir testes sem alterar a máquina do desenvolvedor.

## Componentes

| Projeto                  | Responsabilidade                                                    | Não deve conhecer                                        |
| ------------------------ | ------------------------------------------------------------------- | -------------------------------------------------------- |
| `FiveMCleaner.App`       | WPF, navegação, prévia, progresso e confirmação                     | APIs administrativas ou detalhes de registro             |
| `FiveMCleaner.Contracts` | DTOs, IDs, estados, erros e contratos entre processos               | WPF ou implementação Windows                             |
| `FiveMCleaner.Core`      | casos de uso, composição de perfis, políticas, transação e rollback | controles visuais ou comandos shell                      |
| `FiveMCleaner.Windows`   | descoberta de hardware/instalação e adaptadores Windows/FiveM       | decisão de qual perfil o usuário deve escolher           |
| `FiveMCleaner.Broker`    | executor elevado com allowlist mínima                               | navegação, telemetria ou lógica de produto ampla         |
| `FiveMCleaner.Tests`     | contratos, políticas, falhas, rollback e doubles de sistema         | dependência de uma instalação real para testes unitários |

## Fronteira de confiança

```mermaid
flowchart LR
  U["Usuário"] --> A["App WPF · usuário padrão"]
  A --> C["Core · plano e políticas"]
  C --> W["Windows adapters · operações sem elevação"]
  C --> K["Contracts · mensagens tipadas"]
  K -->|"consentimento + UAC"| B["Broker elevado · allowlist"]
  W --> F["FiveM Legacy e Windows"]
  B --> S["Configurações administrativas permitidas"]
  C --> R["Snapshots e relatório local"]
  W -. "Enhanced detectado" .-> X["Bloqueio seguro"]
```

O broker não é uma “shell como administrador”. Contratos não carregam scripts nem comandos livres.

## Modelo de domínio

### Diagnóstico

Um snapshot de diagnóstico deve conter fatos, não recomendações:

- edição e caminho canônico da instalação;
- versão conhecida do cliente;
- processos ativos relacionados ao diretório;
- CPU, RAM, GPU, VRAM, sistema e espaço livre;
- presença e tamanho de caches reconhecidos;
- estado das configurações suportadas;
- alertas de ambiguidade, permissão ou corrupção.

Políticas do Core transformam esse snapshot em recomendações.

### Ação

Cada ação tem contrato equivalente a:

```text
id + versão
descrição e evidência
escopo de leitura/escrita
pré-condições (incluindo pré-requisitos de outras ações, quando existem)
estado atual e estado desejado
risco, privilégio e criticidade (aborta o restante da execução se falhar?)
aplicar + verificar + restaurar
progresso por etapas
versões do Windows suportadas
documentação: como detectar, como confirmar, como desfazer, riscos/limitações
```

IDs são estáveis para que relatórios e snapshots continuem interpretáveis entre versões. Os campos de pré-requisito, criticidade, versões do Windows e documentação vivem em `ActionMetadataDto`/`OptimizationActionDefinition` e alimentam tanto o motor de execução quanto a revisão do plano na interface.

### Plano

Um plano é uma lista ordenada e imutável de ações resolvidas para aquele diagnóstico. Depois que o usuário confirma:

- nenhuma ação nova pode ser adicionada;
- caminhos não podem ser recalculados para outro alvo;
- conflito entre ações invalida o plano;
- o broker recebe somente o subconjunto privilegiado já aprovado.

### Resultado

`ActionExecutionOutcome` (`FiveMCleaner.Contracts`) é o estado semântico usado por progresso e relatório:

- `Verified` — máquina já estava no estado desejado; nenhuma escrita ocorreu;
- `Applied` — alteração e pós-condição confirmadas;
- `Skipped` — pré-condição, opção ou pré-requisito ausente, sem erro;
- `Warning` — aplicado com ressalva reportável;
- `Failed` — erro genuíno; a própria ação foi revertida;
- `RolledBack` — revertida com sucesso após falha;
- `RollbackFailed` — requer atenção e fica destacado no relatório;
- `Blocked` — edição/segurança não suportada;
- `NotRun` — não executada porque uma falha crítica anterior abortou o restante da run.

Esse enum é independente do estado transacional interno do journal
(`WindowsActionJournalState`), que continua controlando elegibilidade de
rollback e resumo de transação.

## Perfis

Leve, Médio e Agressivo são seleções versionadas de ações e parâmetros. Eles não implementam operações diretamente.

```text
Perfil → Política de hardware → Ações propostas → Prévia do usuário → Plano imutável
```

Isso permite:

- desmarcar uma ação sem criar um quarto perfil;
- testar cada ação isoladamente;
- comparar versões de um perfil;
- impedir que “Agressivo” se torne sinônimo de mudanças irreversíveis.

Cache é um módulo de manutenção separado e não entra implicitamente nesses perfis.

## Adaptador FiveM Legacy

Responsabilidades:

- localizar instalação padrão e personalizada;
- validar `CitizenFX.ini` e `IVPath` sem reescrevê-los por conveniência;
- mapear somente diretórios conhecidos sob `FiveM.app`;
- identificar processos por caminho da imagem, não só por nome;
- ler e editar `gta5_settings.xml` preservando schema e nós desconhecidos;
- proteger `game-storage`, `nui-storage`, plugins e autenticação;
- calcular tamanho de caches sem segui-los para fora do root canônico.

O parser XML altera apenas chaves presentes. Um arquivo inválido gera ação de reparo separada; nunca é substituído por um template genérico.

## Guard de GTAV Enhanced

O Enhanced tem launcher, ciclo de processo e cache diferentes. Até o adaptador próprio existir:

1. a descoberta identifica sinais inequívocos da edição;
2. o Core retorna `Blocked` com explicação;
3. nenhum fallback Legacy é tentado;
4. o usuário recebe links para o estado de suporte do projeto;
5. testes garantem que nenhum executor seja chamado.

Quando o suporte for implementado, ele deve ser um adaptador separado e passar por nova pesquisa de caminhos, rollback e políticas.

## Execução, progresso e cancelamento

Progresso é calculado por passos concluídos e pesos declarados. Mensagens devem descrever ações reais, por exemplo “Validando snapshot gráfico”, não frases genéricas. O progresso também expõe etapa atual / total de etapas (`CompletedSteps`/`TotalSteps` em `WindowsActionProgress` e `AppProgressUpdate`) e o outcome de cada etapa, permitindo à interface manter um livro-razão ao vivo (ver `MainViewModel.StepLedger`).

## Telemetria opcional

`IAnonymousTelemetryService` é uma fronteira da camada App, separada do
serviço de otimização. A preferência persistida `AppSettings.ShareAnonymousTelemetry`
nasce como `false`; o `MainViewModel` só gera um evento ao término de uma
otimização após o consentimento. O contrato `AnonymousTelemetryEvent` não
aceita payload livre: contém apenas nome allowlisted do evento, duração, versão
e, em falha, categoria allowlisted. A implementação FormSubmit é best-effort;
qualquer erro é suprimido localmente para não alterar a execução nem os logs.
Detalhes de privacidade: [telemetry.md](telemetry.md).

## Interrupção de otimização pela interface

O `MainWindow` não encerra nem chama `MainViewModel.CancelOptimization()`
diretamente enquanto `IsBusy` for verdadeiro. Ambos os caminhos de interface
(botão de cancelar e fechamento da janela, inclusive pelo ícone da bandeja)
passam por `OptimizationConfirmationWindow`, um modal localizado e temático.
Ao confirmar, o view-model solicita o token de cancelamento já existente; a
execução mantém a garantia de concluir ou reverter a etapa atual. Um fechamento
confirmado agenda o encerramento somente depois que `StartOptimizationAsync`
retorna. O evento de sessão do Windows é exceção: não mostra modal e não impede
logoff/desligamento.

A execução do usuário padrão roda com `WindowsTransactionOptions.IsolateFailures = true`: cada ação do plano é aplicada, validada e registrada como uma mini-transação independente.

- uma falha genuína reverte somente a própria ação (rollback atômico existente, sem afetar as demais);
- uma ação cujo pré-requisito não teve sucesso (`Prerequisites` em `ActionMetadataDto`) é marcada `Skipped`, nunca executada;
- uma ação crítica (`IsCritical`, hoje as verificações de processo FiveM/GTA V) que falha aborta as ações independentes restantes, que ficam `NotRun`;
- a transação final é `Committed` somente se nenhuma ação falhou; caso contrário `CommittedWithErrors`, e o relatório (`OptimizationReportDto`, construído por `OptimizationReportBuilder`) nunca marca a run como bem-sucedida.
- o broker elevado continua no modo estrito (tudo-ou-nada), pois normalmente delega uma única ação administrativa por vez.

Cancelamento:

- é aceito antes de iniciar uma ação ou depois de um passo atômico;
- uma escrita crítica termina ou restaura antes de honrar o cancelamento;
- ações não canceláveis declaram isso na prévia;
- o relatório diferencia cancelamento limpo de falha.

## Persistência

O MVP grava somente sob `%LOCALAPPDATA%\FiveMCleaner`:

- `Transactions/<id>.json`: plano, estados por ação e snapshots pequenos necessários ao rollback;
- `Requests/<id>.json`: solicitação efêmera e de uso único consumida atomicamente pelo broker;
- `settings.json`: preferências do próprio FiveMCleaner;
- `crash.log`: exceções fatais locais, criado apenas quando necessário.

Caches não são copiados para o journal. Durante uma limpeza, arquivos allowlisted são movidos para uma quarentena dentro do próprio volume; a ação restaura essa quarentena se falhar antes do commit e a remove somente ao confirmar a transação.

## Testabilidade

Adaptadores de sistema ficam atrás de interfaces. Testes devem cobrir:

- caminhos fora do root e reparse points;
- instalação personalizada;
- FiveM ativo durante uma ação;
- Enhanced bloqueado;
- XML válido, desconhecido e corrompido;
- falha antes, durante e depois de uma escrita;
- rollback que restaura tipo, existência e conteúdo;
- falta de espaço para snapshot/quarentena;
- broker rejeitando ação, versão ou alvo desconhecido;
- composição de perfis sem cache implícito;
- mensagens de progresso e cancelamento;
- execução isolada: falha não crítica não afeta ações independentes; falha
  crítica aborta o restante (`NotRun`); pré-requisito não atendido gera
  `Skipped`; falha de commit reverte só a própria ação;
- construção do relatório estruturado e sanitização do relatório técnico
  copiável (sem nome de usuário em caminhos, sem segredos).

Testes de integração que alteram Windows ou FiveM devem ser opt-in, isolados e nunca rodar automaticamente na máquina do contribuidor.

## Distribuição

O pipeline público deve:

- compilar no Windows com o SDK fixado em `global.json`;
- executar testes em Release;
- produzir artefatos determinísticos;
- assinar releases oficiais quando houver infraestrutura de assinatura;
- publicar checksums junto ao código-fonte correspondente;
- não realizar self-update arbitrário nem baixar payloads executáveis.

## Não objetivos

- competir com antivírus ou ferramentas de manutenção geral;
- “debloat” irrestrito do Windows;
- modificar servidores ou recursos de terceiros;
- burlar pure mode, anti-cheat ou integridade;
- consertar scripts/assets ruins do servidor pelo cliente;
- suportar GTAV Enhanced reutilizando suposições do Legacy.
