// The code is deliberately short for a table or a support ticket. The label
// makes it actionable without sending exception text, paths, or stack traces.
export const UPDATER_EVENT_CATALOG = Object.freeze({
  u000: ['Atualização confirmada', 'Concluída'],
  u101: ['Manifesto: origem ou resposta recusada', 'Manifesto'],
  u102: ['Manifesto: tamanho acima do limite', 'Manifesto'],
  u103: ['Manifesto: formato inválido', 'Manifesto'],
  u104: ['Manifesto: assinatura ou confiança inválida', 'Manifesto'],
  u201: ['Pacote: origem fora da rota oficial', 'Download'],
  u202: ['Pacote: redirecionamento recusado', 'Download'],
  u203: ['Pacote: resposta HTTP inesperada', 'Download'],
  u204: ['Pacote: tamanho diferente do manifesto', 'Download'],
  u205: ['Pacote: hash SHA-256 diferente do manifesto', 'Download'],
  u301: ['Preparação: falha ao montar o pacote', 'Preparação'],
  u302: ['Preparação: integridade do pacote inválida', 'Preparação'],
  u303: ['Preparação: caminho do instalador recusado', 'Preparação'],
  u304: ['Preparação: metadados do instalador inválidos', 'Preparação'],
  u305: ['Preparação: cópia do atualizador sem integridade', 'Preparação'],
  u401: ['Ativação: falha ao trocar a versão ativa', 'Ativação'],
  u402: ['Ativação: launcher não iniciou', 'Ativação'],
  u403: ['Ativação: processo anterior não encerrou', 'Ativação'],
  u404: ['Ativação: runtime ativo inválido', 'Ativação'],
  u501: ['Recuperação: inicialização saudável não confirmada', 'Recuperação'],
  u502: ['Recuperação: rollback falhou', 'Recuperação'],
  u601: ['Sistema: acesso negado pelo Windows', 'Sistema'],
  u602: ['Sistema: falha local de leitura ou escrita', 'Sistema'],
  u701: ['Rede: solicitação ao servidor falhou', 'Rede'],
  u702: ['Rede: solicitação expirou', 'Rede'],
  u999: ['Falha não classificada', 'Outros'],
});

// Releases anteriores enviam estes valores. Eles permanecem legíveis e válidos
// para que diagnósticos pendentes não sejam descartados neste deploy.
export const LEGACY_UPDATER_EVENT_CATALOG = Object.freeze({
  'access-denied': ['Cliente anterior: acesso negado', 'Legado'],
  healthy: ['Cliente anterior: atualização confirmada', 'Legado'],
  'health-timeout': ['Cliente anterior: saúde da atualização expirou', 'Legado'],
  'invalid-data': ['Cliente anterior: dados inválidos', 'Legado'],
  io: ['Cliente anterior: falha local de I/O', 'Legado'],
  network: ['Cliente anterior: falha de rede', 'Legado'],
  'rollback-failed': ['Cliente anterior: rollback falhou', 'Legado'],
  'security-policy': ['Cliente anterior: política de segurança recusou', 'Legado'],
  'signature-invalid': ['Cliente anterior: assinatura inválida', 'Legado'],
  timeout: ['Cliente anterior: operação expirou', 'Legado'],
  unexpected: ['Cliente anterior: falha não classificada', 'Legado'],
});

export const ALLOWED_UPDATER_EVENT_CODES = new Set([
  ...Object.keys(UPDATER_EVENT_CATALOG),
  ...Object.keys(LEGACY_UPDATER_EVENT_CATALOG),
]);

export function describeUpdaterEventCode(errorCode) {
  const [errorName, errorGroup] = UPDATER_EVENT_CATALOG[errorCode]
    ?? LEGACY_UPDATER_EVENT_CATALOG[errorCode]
    ?? ['Código não reconhecido', 'Outros'];
  return {
    error_id: errorCode.toUpperCase(),
    error_name: errorName,
    error_group: errorGroup,
  };
}
