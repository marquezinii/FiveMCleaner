const PRO_ENTITLEMENT = 'ralven_pro';
const AI_ENTITLEMENT = 'ralven_ai';

/**
 * Reads the server-authoritative access snapshot for the verified Firebase UID.
 * Provider and subscription identifiers are deliberately neither selected nor
 * returned to the client.
 *
 * @param {D1Database} db
 * @param {string} uid
 * @param {string} [nowIso]
 * @returns {Promise<{
 *   tier: 'free' | 'pro', entitlements: string[], validFrom: string | null,
 *   validUntil: string | null, aiValidFrom: string | null, aiValidUntil: string | null
 * }>}
 */
export async function fetchAccountEntitlements(db, uid, nowIso = new Date().toISOString()) {
  const result = await db
    .prepare(
      `SELECT entitlement_key, valid_from, valid_until
       FROM account_entitlements
       WHERE account_uid = ?
         AND entitlement_key IN (?, ?)
         AND state IN ('active', 'grace_period')
         AND valid_from <= ?
         AND valid_until > ?
       ORDER BY entitlement_key`,
    )
    .bind(uid, PRO_ENTITLEMENT, AI_ENTITLEMENT, nowIso, nowIso)
    .all();

  const rows = Array.isArray(result?.results) ? result.results : [];
  const pro = rows.find(row => row.entitlement_key === PRO_ENTITLEMENT);
  const ai = pro && rows.find(row => row.entitlement_key === AI_ENTITLEMENT);

  return pro === undefined
    ? {
      tier: 'free', entitlements: [], validFrom: null, validUntil: null,
      aiValidFrom: null, aiValidUntil: null,
    }
    : {
      tier: 'pro',
      entitlements: ai === undefined ? [PRO_ENTITLEMENT] : [PRO_ENTITLEMENT, AI_ENTITLEMENT],
      validFrom: pro.valid_from,
      validUntil: pro.valid_until,
      aiValidFrom: ai?.valid_from ?? null,
      aiValidUntil: ai?.valid_until ?? null,
    };
}
