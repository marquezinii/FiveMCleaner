# Objetivo da tarefa

- **Agente**: Codex
- **Objetivo**: identificar a conta autenticada no cabeçalho pelo username do
  perfil, com avatar circular valorizado e apresentação compacta coerente com o
  design system existente.

## Escopo

Pertence à tarefa:

- substituir o e-mail pelo username do perfil quando houver sessão autenticada;
- reutilizar o carregamento de perfil e o avatar local existentes;
- impedir que resposta assíncrona antiga de perfil seja aplicada a outra sessão;
- ajustar somente o componente de conta do cabeçalho, incluindo truncamento,
  forma circular, moldura, temas e acessibilidade;
- preservar o fallback de avatar e o convite de autenticação atuais;
- adicionar o menor teste de regressão útil para o comportamento alterado.

Não pertence à tarefa:

- redesign global ou varredura estética do aplicativo;
- alterações em autenticação, backend, contratos remotos ou armazenamento de
  avatar;
- novas dependências, abstrações especulativas ou mudança de versão/release.

## Critérios de conclusão

- cabeçalho autenticado mostra username compacto, nunca e-mail;
- nome longo não amplia o cabeçalho e avatar/fallback usam o mesmo círculo;
- troca de sessão durante o fetch não mistura usernames;
- estado desautenticado e card de Configurações permanecem funcionais;
- build Release, suíte .NET e validações aplicáveis aprovam;
- inspeção visual em claro/escuro e autenticado/desautenticado é executada
  quando o ambiente permitir.

## Resultado entregue

- O cabeçalho autenticado reutiliza o username retornado pelo fetch de perfil,
  nunca o `DisplayName`/e-mail do Firebase, e descarta respostas de outro UID.
- Username longo usa elipse em 120 px; o convite deslogado preserva 160 px.
- Avatar salvo e fallback existente compartilham um círculo de 32 px, com foto
  de 28 px, superfície e borda vindas dos tokens claro/escuro.
- O nome acessível do controle acompanha a ação localizada de entrar ou ver a
  conta.
- O harness de captura ganhou fixture de username restrita a demo/captura para
  validar deterministicamente os estados visuais sem credenciais reais.

Validação executada:

- teste focado: 1 aprovado, 0 falhas;
- build Release: sucesso, 0 avisos;
- suíte .NET: 1.435 aprovados, 0 falhas;
- `dotnet format Ralven.slnx --verify-no-changes --no-restore`: limpo;
- `scripts\Verify-Safety.ps1`: aprovado;
- `git diff --check`: limpo;
- capturas inspecionadas em claro/escuro, autenticado/desautenticado;
- `scripts\Install-DevelopmentShortcut.ps1 -Build`: atalho reconstruído.
