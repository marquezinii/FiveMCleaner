# Cache e dados locais do Ralven

O Ralven mantém seus dados por usuário em `%LOCALAPPDATA%\Ralven`, sem depender
do caminho em que os binários foram instalados. A limpeza manual fica em
**Configurações > Ferramentas > Cache do Ralven** e trabalha somente sobre a
allowlist desta página.

## Classificação auditada

| Classe | Caminhos | Limpeza manual |
| --- | --- | --- |
| Cache seguro | `Cache\**` | Sim. Conteúdo reservado para dados regeneráveis. |
| Downloads temporários | `Updates\**` | Sim. Pacotes assinados já podem ser baixados novamente; uma atualização em andamento mantém o arquivo aberto e produz resultado parcial. |
| Logs descartáveis | `Logs\**` e `Telemetry\pending\telemetry_failures.log` | Sim. A remoção perde somente contexto de diagnóstico local. |
| Temporários | `Temp\**`, `Requests\**`, temporários atômicos conhecidos em `Personal`, `avatars` e `Updater`, além de `.settings.*.tmp`, `.application-update-ignores.*.tmp` e resíduos `.importing` reconhecidos | Sim. São sobras de operações interrompidas ou solicitações efêmeras. |
| Dados persistentes do usuário | `settings.json`, `application-update-ignores.json`, `avatars\*.png`, `Personal\workspace.json` e `history.json` legado | Não. Guardam escolhas, foto, histórico e medições locais. |
| Autenticação e privacidade | `firebase.session`, `Telemetry\pending\*.json` e `UpdaterTelemetry\pending\*.json` | Não. A sessão é protegida por DPAPI e as filas representam eventos pendentes sujeitos à preferência de privacidade, não cache. |
| Segurança, auditoria e rollback | `Transactions\**`, `AuthQuarantine\**`, `UpdateSecurity\**` e `.legacy-data-import-v1` | Não. São necessários para auditoria, restauração, anti-downgrade e migração idempotente. |
| Arquivos necessários ao funcionamento | `Updater\Ralven.Updater.exe`, diretório de instalação, `Runtime\active.json`, versão ativa e predecessor imediato | Não. São executáveis ou estado da cadeia transacional de atualização. |

Os diretórios descartáveis equivalentes das gerações conhecidas `Vemryx One` e
`FiveMCleaner` também entram na allowlist. Seus arquivos persistentes continuam
preservados. Isso elimina resíduos conhecidos sem varrer nomes ou diretórios
arbitrários no perfil do usuário.

## Contrato da limpeza

- o tamanho mostrado soma somente arquivos allowlisted e nunca segue links ou
  reparse points;
- cada arquivo e seu ancestral são revalidados imediatamente antes da exclusão;
- arquivo bloqueado, sem permissão ou inacessível é preservado e gera resultado
  parcial; o restante continua sendo processado;
- a operação é idempotente: repetir sobre um cache vazio libera zero bytes sem
  erro;
- nenhuma exclusão atravessa o root exato que classificou o arquivo;
- arquivos de cache são regeneráveis e a exclusão é definitiva, sem ocupar
  espaço duplicado em quarentena.

## Microsoft PC Manager

O instalador registra o Ralven como aplicativo Win32 por usuário com `AppId`
estável, `DisplayName`, versão, publicador, localização, ícone oficial e atalhos
com o `AppUserModelID` estável `Ralven.Ralven`. Isso fornece ao Windows a
identidade visual suportada e independente do caminho de instalação.

O Microsoft PC Manager informa que a Limpeza Profunda examina arquivos,
registro e metadados locais para reconhecer caches de aplicativos, mas não
publica API, manifesto ou contrato de terceiros para cadastrar caminhos na lista
**Outros itens do aplicativo**. `AppUserModelID` identifica aplicativos para
inicialização, alternância e recursos do shell; ele não registra cache.
Consequentemente, a aparição nessa lista depende da heurística e da versão do
próprio PC Manager e não pode ser prometida pelo Ralven sem uma integração não
documentada.

Fontes oficiais:

- [Microsoft PC Manager — termos e funcionamento da limpeza](https://pcmanager.microsoft.com/en-us/termsofservice)
- [Microsoft Learn — Application User Model ID](https://learn.microsoft.com/windows/configuration/store/find-aumid)
- [Inno Setup — AppUserModelID em atalhos](https://jrsoftware.org/ishelp/topic_iconssection.htm)
- [Microsoft Learn — metadados de aplicativos instalados](https://learn.microsoft.com/windows/win32/msi/uninstall-registry-key)

Não são criadas chaves artificiais, handlers de limpeza, pacotes esparsos ou
identidades MSIX apenas para tentar influenciar um scanner fechado. Se a
Microsoft publicar um contrato específico no futuro, esta limitação deve ser
reavaliada contra a documentação oficial antes de qualquer integração.
