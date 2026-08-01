const STAGES = new Set(['manifest', 'download', 'staging', 'activation', 'health-check', 'rollback']);
const OUTCOMES = new Set(['failed', 'rolled-back', 'recovered', 'completed']);
const VERSION = /^\d+\.\d+\.\d+$/;
const SAFE_CODE = /^[a-z0-9][a-z0-9._-]{0,63}$/;

export function validateUpdaterEvent(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const keys = Object.keys(value);
  if (keys.some((key) => !['eventId', 'stage', 'outcome', 'errorCode', 'previousVersion', 'candidateVersion', 'environment'].includes(key))) return null;
  if (!/^[a-f0-9]{32}$/.test(value.eventId) || !STAGES.has(value.stage) || !OUTCOMES.has(value.outcome)
    || typeof value.errorCode !== 'string' || !SAFE_CODE.test(value.errorCode) || !VERSION.test(value.candidateVersion)
    || (value.previousVersion !== null && !VERSION.test(value.previousVersion))
    || !['Development', 'Production'].includes(value.environment)) return null;
  return value;
}
