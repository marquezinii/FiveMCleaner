// Pure, dependency-free validation for one admin live-alert update. Mirrors
// bugReports/validateSubmission.js: the Worker never trusts the client
// alone, every field is re-checked here.
//
// `message` is optional so the dashboard's "Desativar" action can flip
// `active` off without resending the stored text.

export const MAX_LIVE_ALERT_MESSAGE_LENGTH = 300;

export function validateLiveAlertUpdate(payload) {
  if (typeof payload !== 'object' || payload === null) {
    return null;
  }

  const { message, active } = payload;

  if (typeof active !== 'boolean') {
    return null;
  }

  if (message === undefined) {
    return { active };
  }

  if (typeof message !== 'string') {
    return null;
  }

  const trimmed = message.trim();
  if (trimmed.length > MAX_LIVE_ALERT_MESSAGE_LENGTH) {
    return null;
  }
  if (active && trimmed.length === 0) {
    return null;
  }

  return { message: trimmed, active };
}
