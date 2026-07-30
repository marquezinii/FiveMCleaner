# Operação da telemetria em produção

## Contrato e fluxo

O aplicativo envia lotes JSON por `POST` para a rota HTTPS `/telemetry` do
Worker Cloudflare. Cada evento deve conter `eventName`, `executionTimeMs`,
`appVersion` e `environment` (`Development` ou `Production`), além dos campos
opcionais documentados em `telemetry.md`. O Worker valida o lote inteiro,
persiste-o no D1 `fivemcleaner-telemetry` e retorna `202 Accepted`.

O dashboard usa a mesma origem do Worker e filtra `Production` por padrão.
Assim, um evento marcado como `Development` não aparece no painel padrão até
que o filtro de ambiente seja alterado.

## Incidente de 30/07/2026

A versão pública `1.1.0` construía o transporte Cloudflare sem incluir o
campo obrigatório `environment`. O Worker corretamente recusava todos esses
lotes com HTTP `400`; como o cliente tratava qualquer resposta não-2xx como
falha transitória, os eventos permaneciam na fila local e nenhum aparecia no
dashboard. A correção inclui o ambiente resolvido pelo executável em todo
payload e rejeita configurações de produção que não apontem para o host e a
rota autorizados.

## Checklist obrigatório antes de uma release

- confirme que `Config/appsettings.Production.json` está no publish e aponta
  para `https://fivemcleaner-telemetry.felipemarquesini10.workers.dev/telemetry`;
- valide a serialização do cliente, incluindo `environment: Production`;
- execute `npm.cmd test` em `infra/cloudflare-worker` e `infra/dashboard`;
- execute o build e os testes Release do .NET;
- usando credenciais Cloudflare, execute uma consulta remota ao D1 para
  confirmar a migration e o último evento recebido;
- instale o artefato Release, dê consentimento e execute uma otimização
  controlada; registre versão, horário UTC e identificador de smoke test;
- confirme o `202` no Worker, a linha no D1 e a visibilidade no dashboard;
- confirme que o artefato do instalador contém a build recém-validada.

## Diagnóstico seguro

Não envie telemetria ao Sentry e não use FormSubmit como fallback. Telemetria
anônima usa apenas Worker + D1; crashes usam Sentry após consentimento próprio;
relatos manuais usam `/bugs`. Uma resposta 2xx, incluindo 202, remove o lote
da fila. Falhas de rede, 429 e 5xx ficam na fila para nova tentativa; rejeições
4xx permanentes são descartadas para não criar retry infinito.

## Comandos remotos (exigem autenticação Cloudflare)

```powershell
npx.cmd wrangler d1 execute fivemcleaner-telemetry --remote --command "SELECT COUNT(*) AS total, MAX(received_at) AS last_received FROM telemetry_events"
npx.cmd wrangler deployments list --name fivemcleaner-telemetry
```

Nunca coloque `CLOUDFLARE_API_TOKEN`, segredos do Worker ou credenciais D1 no
aplicativo, em arquivos versionados ou em logs.
