# Modelo de segurança

O FiveMCleaner altera configurações de alto impacto potencial. Segurança, explicabilidade e reversão são requisitos funcionais; não são uma tela de aviso adicionada depois.

## Invariantes

Uma ação aceita pelo produto precisa respeitar todos os itens abaixo:

1. **Escopo conhecido** — instalação e edição foram identificadas sem ambiguidade.
2. **Legacy somente** — GTAV Enhanced retorna bloqueio seguro.
3. **Processos encerrados** — nenhuma escrita ou limpeza ocorre com processos FiveM ativos.
4. **Alvo canônico** — o caminho final foi resolvido e permanece dentro do diretório esperado.
5. **Privilégio mínimo** — elevação acontece apenas para uma operação administrativa tipada.
6. **Prévia completa** — o usuário vê o que será alterado, por quê, risco e rollback.
7. **Operação idempotente** — repetir a ação não amplia seu escopo nem degrada o sistema.
8. **Registro local** — início, resultado, falha e restauração ficam auditáveis, sem dados sensíveis.
9. **Cancelamento seguro** — somente entre passos atômicos; nunca no meio de uma escrita crítica.
10. **Sem promessa universal** — a interface informa efeito esperado, não FPS garantido.

## Ações proibidas

O projeto não aceita implementações que:

- desativem Defender, firewall, SmartScreen, UAC ou antivírus de terceiros;
- adicionem exclusões de antivírus automaticamente ou sugiram desativar a proteção;
- injetem código, leiam/escrevam memória do FiveM ou modifiquem binários do GTA/FiveM;
- executem PowerShell, CMD ou scripts remotos arbitrários por meio do broker;
- apliquem prioridade `Realtime`, afinidade fixa ou desliguem SMT/Hyper-Threading;
- usem “debloat” genérico, removam AppX em massa ou desativem serviços sem relação comprovada;
- editem `commandline.txt` como otimização do FiveM;
- sobrescrevam perfil NVIDIA ou ativem/limpem shader cache à força;
- removam dados de autenticação, entitlement, plugins ou configurações em perfis automáticos;
- escondam ações, usem ofuscação ou baixem código executável depois da instalação;
- contornem anti-cheat, pure mode ou verificações de integridade;
- operem em FiveM/GTAV Enhanced enquanto esse adaptador estiver bloqueado.

## Proteção de caminhos

### Nunca remover automaticamente

- `FiveM.app\data\game-storage`;
- `FiveM.app\data\nui-storage`;
- `FiveM.app\data\ipfs`;
- `FiveM.app\CitizenFX.ini`;
- `FiveM.app\plugins`;
- `%APPDATA%\CitizenFX\gta5_settings.xml`;
- `%APPDATA%\CitizenFX\fivem.cfg`;
- qualquer `fivem_set.bin`;
- `%APPDATA%\CitizenFX\ros_id.dat`;
- `%LOCALAPPDATA%\DigitalEntitlements`;
- arquivos da instalação original do GTAV.

Configurações podem ser editadas por uma ação tipada, mas nunca tratadas como lixo.

### Limpeza condicionada

| Alvo                                | Condição                                         | Aviso obrigatório                                           |
| ----------------------------------- | ------------------------------------------------ | ----------------------------------------------------------- |
| `data\server-cache`                 | FiveM encerrado; usuário abriu manutenção/reparo | recursos serão baixados novamente                           |
| `data\server-cache-priv`            | mesmas condições                                 | clipes antigos do Rockstar Editor podem deixar de funcionar |
| `crashes`                           | dumps não serão enviados ao suporte              | dumps podem ser essenciais para diagnóstico                 |
| `logs`                              | somente arquivos antigos e reconhecidos          | logs recentes devem ser preservados                         |
| `content_index.xml` ou `caches.xml` | erro de integridade/componente correspondente    | FiveM fará nova verificação/download                        |

A limpeza de cache não entra implicitamente nos modos Leve, Médio ou Agressivo.

## Ciclo transacional

Cada execução segue o mesmo protocolo:

```text
Descobrir → Planejar → Validar → Criar snapshot → Aplicar → Verificar → Confirmar
                                          ↘ falha → Restaurar → Relatar
```

### Descobrir

- localizar a instalação padrão ou personalizada;
- canonicalizar caminhos e resolver links/reparse points;
- identificar Legacy versus Enhanced;
- obter versão do Windows, espaço livre, GPU, VRAM e RAM;
- detectar processos cuja imagem pertence à instalação FiveM.

### Planejar

O plano é imutável depois da confirmação e contém:

- identificador e versão de cada ação;
- estado observado e estado desejado;
- arquivos/valores que poderão ser tocados;
- necessidade de privilégio e reinício;
- estimativa de espaço recuperável;
- risco, evidência e estratégia de rollback.

### Criar snapshot

- arquivos pequenos são copiados com metadados e hash;
- valores Windows preservam tipo e existência, não apenas conteúdo;
- XML é validado antes e depois da cópia;
- caches grandes não são duplicados silenciosamente;
- quando há espaço, uma limpeza pode usar quarentena no mesmo volume;
- sem espaço para quarentena, a exclusão irreversível exige confirmação explícita.

### Aplicar e verificar

- usar escrita temporária e troca atômica para configurações;
- conferir pós-condições de cada ação;
- interromper a sequência ao primeiro erro não recuperável;
- não reportar sucesso parcial como otimização concluída;
- restaurar automaticamente o passo atual quando a pós-condição falhar.

### Restaurar

Rollback precisa ser testável e simétrico. Restaurar significa recuperar:

- conteúdo e localização do arquivo;
- valor, tipo e existência de configuração;
- seleção de perfil e campos gráficos alterados;
- estado de energia somente se a aplicação o criou ou modificou.

Cache já removido sem quarentena é explicitamente marcado como não reversível; sua recuperação ocorrerá por novo download do FiveM.

## Broker elevado

A interface e a maior parte do motor executam sem elevação. O broker administrativo:

- recebe contratos tipados e versionados;
- não aceita linha de comando ou script arbitrário;
- restringe o pipe ao usuário atual e valida o identificador efêmero da sessão, a edição e o alvo novamente;
- usa allowlist de ações administrativas;
- resolve caminhos do próprio lado;
- encerra quando a sequência privilegiada termina;
- retorna resultado estruturado, sem texto usado como comando subsequente.

Uma operação em arquivos `%LOCALAPPDATA%` ou `%APPDATA%` normalmente não precisa do broker.

## Compatibilidade com antivírus

Não é possível garantir ausência de falsos positivos em todos os produtos. O processo de distribuição deve reduzir superfície suspeita:

- binários e instalador assinados;
- builds determinísticos e hashes de release publicados;
- código-fonte correspondente a cada release;
- sem packers, ofuscação, self-update executável ou payload embutido inesperado;
- sem persistência, driver, injeção ou manipulação de processo;
- manifesto do broker com escopo mínimo;
- comunicação clara de cada alteração administrativa.

Uma exceção de antivírus recomendada pelo suporte do FiveM para um erro específico não autoriza o FiveMCleaner a criar essa exceção automaticamente.

## Dados e privacidade

O diagnóstico permanece local por padrão. Relatórios exportados devem:

- remover nome de usuário dos caminhos;
- não incluir tokens, cookies, entitlement ou conteúdo de chat;
- não anexar dumps, ETW traces ou logs sem seleção explícita;
- mostrar uma prévia do pacote antes de salvar ou compartilhar;
- indicar que ETW e dumps podem conter dados sensíveis.

## Comunicação de vulnerabilidades

Não publique exploits ou bypasses em issues. Siga [SECURITY.md](../SECURITY.md).
