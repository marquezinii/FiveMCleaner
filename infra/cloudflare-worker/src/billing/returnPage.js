/** The browser return is informational only; query parameters prove no payment. */
export function billingReturnPage() {
  const nonce = crypto.randomUUID();
  return new Response(`<!doctype html>
<html lang="pt-BR">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <meta name="color-scheme" content="dark light">
  <title>Volte ao aplicativo · Ralven Pro</title>
  <style nonce="${nonce}">
    :root { font-family: "Segoe UI", system-ui, sans-serif; color: #edf2f8; background: #10151d; }
    * { box-sizing: border-box; }
    body { margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 24px; }
    main { width: min(100%, 620px); padding: clamp(24px, 6vw, 52px); border: 1px solid #354151;
      border-radius: 24px; background: #18212d; box-shadow: 0 20px 60px #0003; }
    .brand { margin: 0 0 36px; font-size: 22px; font-weight: 700; letter-spacing: -.5px; }
    .brand span { margin-left: 8px; padding: 4px 9px; border: 1px solid #80b8ff;
      border-radius: 7px; color: #b4d7ff; font-size: 12px; letter-spacing: 1px; }
    h1 { font-size: clamp(27px, 5vw, 36px); line-height: 1.2; letter-spacing: -.8px; margin: 0 0 18px; }
    p, li { font-size: 16px; line-height: 1.65; }
    p { color: #c5d0df; }
    ol { margin: 26px 0; padding-left: 24px; }
    li { padding: 5px 0 5px 6px; }
    .notice { padding: 18px; border-left: 3px solid #8fc2ff; border-radius: 4px; background: #222f40; }
    .footer { margin-bottom: 0; font-size: 14px; color: #b3c0d1; }
    @media (prefers-color-scheme: light) {
      :root { color: #182334; background: #eef2f7; }
      main { background: #fff; border-color: #c7d2df; box-shadow: 0 20px 60px #16325212; }
      p, .footer { color: #41536a; }
      .brand span { color: #174b88; border-color: #3676bf; }
      .notice { background: #edf5ff; border-left-color: #2269b4; }
    }
  </style>
</head>
<body>
  <main>
    <p class="brand">Ralven <span>PRO</span></p>
    <h1>Continue no aplicativo</h1>
    <p>Acompanhe sua assinatura com segurança pelo Ralven. Você pode fechar esta página depois de voltar ao aplicativo.</p>
    <ol>
      <li>Volte ao <strong>Ralven</strong> no seu computador.</li>
      <li>Abra a página <strong>Ralven Pro</strong>.</li>
      <li>Selecione <strong>Atualizar assinatura</strong> para consultar o status.</li>
    </ol>
    <p class="notice">O pagamento ainda pode estar pendente. Esta página não confirma aprovação: o acesso Pro só é liberado após a confirmação do pagamento pelo Asaas.</p>
    <p class="footer">Se o status ainda não mudou, aguarde alguns instantes e atualize novamente no aplicativo. Não inicie outra assinatura para tentar acelerar a confirmação.</p>
  </main>
</body>
</html>`, {
    headers: {
      'Content-Type': 'text/html; charset=utf-8',
      'Cache-Control': 'no-store',
      'Content-Security-Policy': `default-src 'none'; style-src 'nonce-${nonce}'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'`,
      'Referrer-Policy': 'no-referrer',
      'X-Content-Type-Options': 'nosniff',
      'X-Frame-Options': 'DENY',
      'Permissions-Policy': 'camera=(), microphone=(), geolocation=(), payment=()',
    },
  });
}
