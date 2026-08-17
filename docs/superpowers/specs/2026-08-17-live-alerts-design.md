# Aviso ao vivo (live alerts) — design

Data: 2026-08-17

## Objetivo

Permitir que o administrador escreva uma mensagem no painel (`infra/dashboard`)
e ela apareça, em até ~1h, como um aviso minimalista dentro do app WPF de
todos os usuários — útil para avisar sobre bugs conhecidos (ex.: reinstalar
por causa de um bug no updater) ou lembretes (ex.: entrar no Discord oficial).

## Fluxo de dados

```
Dashboard (admin escreve e clica Enviar)
   → POST /admin/live-alert (Worker, protegido por sessão)
        → upsert em D1 (tabela live_alert, linha única id=1)
App do usuário (startup + a cada 1h via DispatcherTimer)
   → GET /live-alert (Worker, público, sem auth)
        → { id, message, active }
   → active && id != último dismissed salvo localmente → banner amarelo + ícone
   → active && id == dismissed → só o ícone (com tooltip) persiste
   → !active → esconde os dois
```

Não há WebSocket/push; é polling simples (startup + 1h), suficiente para o
caso de uso e sem sobrecarregar o Worker gratuito.

## Modelo de estado

Um único "aviso ativo" por vez (não uma lista/histórico). Escrever um novo
aviso substitui o anterior; desativar limpa o `active` mas mantém o texto
salvo para reedição futura.

## 1. Worker (`infra/cloudflare-worker`)

- **Migration aditiva** `0003_live_alert.sql`: tabela `live_alert` com linha
  única (`id=1`), colunas `message TEXT`, `active INTEGER`, `updated_at TEXT`.
  Seed inicial com `active=0`.
- **`GET /live-alert`** — rota pública (mesmo padrão de `GET /update/manifest`),
  sem sessão. Retorna `{ id, message, active }`, onde `id` é o próprio
  `updated_at`, usado pelo app só como identificador de versão do aviso.
  Rate limiter fail-open próprio (mesma família de `withinRateLimit`), ~30/60s
  por IP, só como proteção básica contra abuso.
- **`POST /admin/live-alert`** — protegida por
  `createPasswordAuthProvider(env).requireSession(request)`, igual às rotas de
  stats/bugs. Body `{ message?: string (máx. 300 chars), active: boolean }`.
  Se `message` vier, atualiza o texto; se omitido, só alterna `active` (usado
  pelo botão "Desativar" sem reenviar o texto). Sem rate limit adicional —
  já protegida por sessão de admin único.
- Resposta segue o padrão existente: `{ success: true }` no POST, erros
  `{ error: 'slug' }` com status apropriado.

## 2. Dashboard (`infra/dashboard`)

- Nova seção "Aviso ao vivo" no `index.html`, seguindo a convenção visual
  atual (form + labels + `role="alert"` para erros, pt-BR, sem framework).
- Status atual (ativo/inativo + horário da última atualização), `<textarea>`
  com contador de caracteres (limite 300), botões de atalho com frases
  prontas (hardcoded em `assets/app.js`, sem CRUD) que preenchem o textarea
  para edição.
- Dois botões: **Enviar** (`active:true` + texto atual) e **Desativar**
  (`active:false`, mantém o texto salvo).
- `assets/api.js` ganha `getLiveAlert()` e `setLiveAlert({message, active})`,
  reaproveitando `requestJson()` (cookie de sessão automático).

## 3. App WPF (`src/FiveMCleaner.App`)

- `ILiveAlertService`/`LiveAlertService` em `Services/`, no padrão de
  `SignedManifestUpdateService` (HTTPS validado, payload pequeno, nunca
  lança — falha de rede é no-op silencioso).
- `MainViewModel`: `IsLiveAlertBannerVisible`, `LiveAlertMessage`,
  `IsLiveAlertIconVisible` + `DismissLiveAlertCommand`. Busca no startup
  (`InitializeAsync`, fire-and-forget como o check de update) e em um novo
  `DispatcherTimer` de 1h.
- Persistência do dismiss: novo campo `DismissedLiveAlertId` em `AppSettings`
  (`settings.json` via `IAppOptimizationService`, mesmo mecanismo do
  tema/telemetria) — dispensar não reaparece nem entre reinícios, mas o
  ícone continua enquanto `active=true`.
- UI: banner reaproveitando a estrutura do banner de update existente
  (`MainWindow.xaml`), com os tokens `WarningBaseBrush`/`WarningSurfaceBrush`/
  `WarningBorderBrush` já usados no app, "X" de fechar com nova chave de
  recurso nos 3 `.resx` (`en`, `pt-BR`, `es`). Ícone de triângulo com "!" em
  um canto da title bar, `ToolTip` = mensagem atual, visível independente do
  banner ter sido fechado.

## Edge cases e limites

- Mensagem só texto puro (sem markdown/HTML) — evita risco de injeção e
  mantém o resultado minimalista.
- Falha de rede/parse no app: não altera o estado atual, não mostra erro.
- Primeira execução sem linha na tabela: `GET` retorna `active:false` com
  segurança.

## Testes

- Worker: rota pública (shape/estado inicial) e rota admin (401 sem sessão,
  validação de tamanho, upsert), seguindo o padrão `npm test` existente.
- Dashboard: testes unitários das novas funções de `api.js` (`node --test`).
- App: testes xUnit para `LiveAlertService` (parsing/falha) e para a lógica
  de exibir/dispensar no `MainViewModel`.
- Validação visual do banner/ícone via atalho de desenvolvimento.

## Limitação conhecida (pendência obrigatória)

Publicar isso em produção exige duas ações remotas fora do fluxo normal de
PR, que exigem autorização explícita do usuário antes de serem executadas:

1. aplicar a migration no D1 de produção
   (`wrangler d1 migrations apply fivemcleaner-telemetry --remote`);
2. `wrangler deploy` do Worker com a rota nova.

Até essas duas ações serem executadas, o endpoint `/live-alert` não existe em
produção e a feature fica implementada mas inerte para usuários reais. Isso
fica registrado como pendência obrigatória em `PROJECT_STATE.md`.
