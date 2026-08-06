# Redesenho da janela Entrar / Cadastre-se

- **Agente:** Claude
- **Branch:** `ai/claude/account-window-ux` (worktree
  `C:\Projetos\FiveMCleaner-account-window-ux`)
- **Objetivo:** rodada completa de frontend/UX na janela de conta — X de
  fechar profissional, textos e controles claros, "Esqueci minha senha"
  condicional, verificação real de "repetir senha", olho de mostrar/ocultar
  senha, unicidade de nome de usuário verificada no banco, barra de rolagem
  na extremidade da janela e login com o Google.
- **Status:** pronto para integração.

## Decisões de produto tomadas com o usuário

Três pontos foram decididos explicitamente antes de codar:

1. **Só Google agora.** Google é o único dos três provedores pedidos que o
   Firebase Authentication suporta nativamente via `accounts:signInWithIdp`,
   sem chave administrativa. Discord exigiria custom token assinado no Worker
   com uma service account do Firebase; ficou para depois.
2. **Cfx.re não entra.** A Cfx.re não publica OAuth para terceiros: não há
   registro de aplicativo público — só o `idms.fivem.net` interno do Keymaster
   e o Discourse SSO do fórum, que exige ser admin do Discourse. Um botão sem
   fluxo real seria um botão quebrado; foi omitido em vez de simulado.
3. **Rota pública de disponibilidade de username**, com rate limit por IP.

## Mudanças — Worker (`infra/cloudflare-worker`)

- `src/auth/accountProfile.js`: `normalizeUsername` (compartilha o
  `USERNAME_PATTERN` do insert, então a sonda nunca diz "disponível" para um
  nome que o INSERT recusaria) e `isUsernameAvailable`.
- `src/rateLimit.js` (novo): `rateLimitKey` + `withinRateLimit` sobre o
  binding `[[ratelimits]]` do Cloudflare. **Fail-open deliberado** quando o
  binding não existe (`wrangler dev`, `node --test`) — a rota é uma sonda
  somente-leitura, e derrubá-la por falta de binding seria pior que atendê-la.
- `src/index.js`: rota `GET /account/username-available?u=<nome>`, sem auth
  (ela roda *antes* da conta existir), rate limit checado antes de qualquer
  I/O, resposta booleana pura — nunca quem detém o nome.
- `wrangler.toml`: binding `USERNAME_LOOKUP_LIMITER`, 20 req/60s por IP.
- Testes: `test/rateLimit.test.js` (6) + 3 em `accountProfile.test.js`,
  **159 no total** (eram 150 antes desta tarefa).

A sonda é **consultiva**. O índice UNIQUE em `account_profiles` continua sendo
o único árbitro; um nome pode ser tomado entre a consulta e o cadastro, e
`POST /account/profile` continua devolvendo 409.

## Mudanças — App (`src/FiveMCleaner.App`)

### Janela de conta (`Views/AccountWindow.xaml` / `.xaml.cs`)

Reescrita. O arquivo antigo estava comprimido em linhas de 400+ colunas, o que
era parte do motivo de a tela estar confusa: não dava para ver a hierarquia.

- **X de fechar:** `ExtendsContentIntoTitleBar` + `ui:TitleBar` só com
  `ShowClose`. É o X nativo do Windows — mesma área de clique, mesmo realce
  vermelho, mesma acessibilidade — e a janela ficou arrastável. `Esc` também
  cancela (`OnPreviewKeyDown`).
- **Barra de rolagem na extremidade:** o `ScrollViewer` agora ocupa a largura
  inteira da janela e é o *conteúdo* que carrega a margem lateral de 30px.
  Antes a margem estava no contêiner, então a barra nascia colada nos campos.
- **Hierarquia e textos:** rótulos de seção (`QUEM É VOCÊ`, `DADOS DE ACESSO`),
  `FormLabelStyle`/`FormHintStyle` consistentes, placeholders, e uma cópia por
  modo centralizada em `ApplyModeCopy()` — antes título, botão e link podiam
  ficar dessincronizados após alternar entre entrar e cadastrar.
- **Validação campo a campo:** `ValidateRegistrationFields()` valida em ordem
  de leitura, com uma mensagem por problema e foco no campo culpado. A
  mensagem única antiga ("senha de 12 caracteres, confirme e aceite os
  termos") obrigava o usuário a adivinhar qual das três o bloqueou.
- **Status:** virou um painel com ícone, borda e fundo tingido, em vez de um
  texto solto.

### "Esqueci minha senha" condicional

Nasce oculto. Aparece **apenas** no modo entrar e **apenas** depois de uma
tentativa de login malsucedida; some ao alternar para o cadastro.

Ressalva honesta: o Firebase deliberadamente não informa se foi o e-mail ou a
senha que estava errado (isso permitiria sondar quais endereços têm conta), e
`FirebaseAuthErrorMapper` preserva isso. Então o gatilho implementado é "a
tentativa de entrar falhou", que é o momento em que o link passa a ser útil —
e não "a senha especificamente estava errada", que o servidor não conta.
Senha vazia não conta: ela nem chega ao Firebase, então não revela o link.

### Olho de mostrar/ocultar senha (`Controls/RevealPasswordBox`)

Controle novo, usado em 4 campos (senha no entrar e no cadastrar, repetir
senha, senha atual e nova senha do gerenciamento). O `PasswordBox` do WPF não
expõe forma de renderizar o conteúdo em claro, então o controle mantém um
`PasswordBox` e um `TextBox` irmãos e alterna qual está visível; `Password` lê
sempre do ativo. Um clique mostra, outro oculta, o cursor não se perde e o
valor sobrevive à troca.

### Repetir senha

Já era comparado no submit; o que faltava era o usuário **ver** isso. Agora há
leitura ao vivo ("As senhas coincidem." / "As senhas não coincidem.") com
ícone e cor, mais uma barra de força ligada ao mínimo de 12 caracteres. A
comparação do submit continua sendo a que bloqueia.

### Nome de usuário único

`UsernameBox` consulta a nova rota com debounce de 450 ms, cancelando a sonda
anterior a cada tecla. Estados: verificando / disponível / já em uso. Um
resultado `Unknown` (rate limit, offline, erro) **nunca** é mostrado como
verde — dizer "disponível" segundos antes do cadastro falhar seria pior que
não dizer nada. O 409 do `POST /account/profile` continua tratado e agora
também acende o rótulo vermelho e devolve o foco ao campo.

### Login com o Google (`Services/GoogleOAuthClient.cs`)

Fluxo OAuth 2.0 authorization code + PKCE com redirect de loopback — o que a
Google documenta para apps instalados. O usuário autentica no próprio
navegador, na página real do Google; o app nunca vê a senha e não usa web view
embutida.

Dois detalhes que costumam quebrar esse fluxo, resolvidos:

- usa `TcpListener` cru em vez de `HttpListener`, porque registrar um prefixo
  de `HttpListener` pode exigir URL ACL (prompt de elevação) em algumas
  configurações do Windows; um socket de loopback nunca exige, e o "servidor
  HTTP" aqui só precisa responder um GET;
- o `state` é comparado antes de o código ser usado, e o listener continua
  aceitando conexões até chegar a que traz a resposta (navegadores abrem
  conexões extras para favicon etc.).

O `id_token` da Google é trocado por sessão Firebase em
`FirebaseAuthService.SignInWithGoogleAsync` (`accounts:signInWithIdp`). Como a
Google já verificou o endereço, a conta vai direto para `SignedIn`, sem passo
de confirmação de e-mail. Conta nova cai no passo de perfil com nome e
sobrenome já preenchidos pelo que a Google informou; conta que volta entra
direto. Se a leitura do perfil falhar por rede, a janela **não** força o passo
de perfil — isso arriscaria uma linha duplicada para um nome que o usuário já
possui.

### Configuração

`googleOAuthClientId` / `googleOAuthClientSecret` em `Config/appsettings*.json`
(hoje `null` nos três). Sem client id, `IsConfigured` é `false` e a janela
**esconde** o botão inteiro em vez de oferecer algo que só pode falhar. O
"secret" do tipo *Desktop app* da Google não é um segredo real (acompanha toda
cópia instalada, e a própria Google o documenta assim); quem protege a troca é
o PKCE.

## Pendente para o usuário — necessário para o botão do Google funcionar

1. Google Cloud Console → Credentials → OAuth client ID → tipo **Desktop app**.
2. Firebase Console → Authentication → Sign-in method → habilitar **Google**.
3. Preencher `googleOAuthClientId` (e o secret, se a credencial tiver um) em
   `Config/appsettings.Development.json` e `appsettings.Production.json`.
4. `wrangler deploy` do Worker para publicar `GET /account/username-available`
   e o binding de rate limit. Enquanto não for publicado, o cliente responde
   `Unknown` e simplesmente não mostra o rótulo de disponibilidade — nada
   quebra.

Enquanto (1)–(3) não acontecerem, o botão não aparece; o cadastro por e-mail e
senha funciona normalmente.

## Testes

- **753 testes .NET** (24 novos), build Release sem avisos,
  `dotnet format --verify-no-changes`, `Verify-Safety.ps1` e
  `git diff --check` aprovados.
- **159 testes do Worker** (9 novos).
- `AccountWindowTests` constrói a janela real numa thread STA com os
  dicionários de recurso da aplicação. É a única verificação que de fato
  interpreta o XAML: um `StaticResource` faltando ou um pincel renomeado
  compila e só explodiria quando o usuário abrisse a janela. Cobre o modo
  inicial, a alternância entrar/cadastrar, a regra do "Esqueci minha senha",
  a leitura de coincidência das senhas, o olho de revelar e a ocultação do
  botão do Google quando não configurado.

## Limitações conhecidas

- A metade interativa do `GoogleOAuthClient` (navegador real + conta Google
  real) não é coberta por teste automatizado; exige validação manual assim que
  as credenciais existirem. O que é testado sem elas: um build não configurado
  não abre navegador, não toca a rede e não finge ter autenticado ninguém.
- O rate limit não foi exercitado contra o Worker implantado — o binding
  `[[ratelimits]]` só existe na edge. Localmente o caminho fail-open é o que
  roda, e é o que está testado.
