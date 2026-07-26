// Pure, dependency-free validation for one bug report submission. Mirrors
// FiveMCleaner.App.Services.CloudflareBugReportService's client-side rules --
// the Worker never trusts the client alone, every field is re-checked here.

export const MAX_CATEGORY_LENGTH = 60;
export const MAX_SUMMARY_LENGTH = 120;
export const MIN_SUMMARY_LENGTH = 5;
export const MAX_DESCRIPTION_LENGTH = 8000;
export const MIN_DESCRIPTION_LENGTH = 20;
export const MAX_APP_VERSION_LENGTH = 32;
export const MAX_PROFILE_LENGTH = 32;
export const MAX_TECHNICAL_SUMMARY_LENGTH = 512;
export const MAX_ATTACHMENT_BYTES = 8 * 1024 * 1024;

export const ALLOWED_ENVIRONMENTS = new Set(['Development', 'Production']);

const PNG_MAGIC_BYTES = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

function isValidAppVersion(value) {
  return (
    typeof value === 'string' &&
    value.length > 0 &&
    value.length <= MAX_APP_VERSION_LENGTH &&
    /^[A-Za-z0-9.-]+$/.test(value)
  );
}

function base64ByteLength(base64) {
  const padding = (base64.match(/=+$/) || [''])[0].length;
  return Math.floor((base64.length * 3) / 4) - padding;
}

function isValidAttachment(attachment) {
  if (attachment === undefined || attachment === null) {
    return true;
  }

  if (typeof attachment !== 'object') {
    return false;
  }

  const { fileName, contentType, contentBase64 } = attachment;
  if (contentType !== 'image/png') {
    return false;
  }

  if (
    typeof fileName !== 'string' ||
    !fileName.startsWith('captura-') ||
    !fileName.toLowerCase().endsWith('.png') ||
    fileName.includes('/') ||
    fileName.includes('\\')
  ) {
    return false;
  }

  if (typeof contentBase64 !== 'string' || contentBase64.length === 0) {
    return false;
  }

  const byteLength = base64ByteLength(contentBase64);
  if (byteLength < 8 || byteLength > MAX_ATTACHMENT_BYTES) {
    return false;
  }

  let bytes;
  try {
    bytes = Uint8Array.from(atob(contentBase64.slice(0, 12)), (c) => c.charCodeAt(0));
  } catch {
    return false;
  }

  return PNG_MAGIC_BYTES.every((byte, index) => bytes[index] === byte);
}

/**
 * Validates and normalizes one bug report submission. Returns `null` when
 * it does not match the closed schema -- never throws.
 */
export function validateBugReport(payload) {
  if (typeof payload !== 'object' || payload === null) {
    return null;
  }

  const {
    reportId,
    category,
    summary,
    description,
    appVersion,
    profile,
    technicalSummary,
    environment,
    attachment,
  } = payload;

  if (typeof reportId !== 'string' || reportId.length === 0 || reportId.length > 64) {
    return null;
  }

  if (
    typeof category !== 'string' ||
    category.length === 0 ||
    category.length > MAX_CATEGORY_LENGTH ||
    /[\r\n]/.test(category)
  ) {
    return null;
  }

  if (
    typeof summary !== 'string' ||
    summary.trim().length < MIN_SUMMARY_LENGTH ||
    summary.trim().length > MAX_SUMMARY_LENGTH
  ) {
    return null;
  }

  if (
    typeof description !== 'string' ||
    description.trim().length < MIN_DESCRIPTION_LENGTH ||
    description.trim().length > MAX_DESCRIPTION_LENGTH
  ) {
    return null;
  }

  if (!isValidAppVersion(appVersion)) {
    return null;
  }

  if (typeof profile !== 'string' || profile.length === 0 || profile.length > MAX_PROFILE_LENGTH) {
    return null;
  }

  if (
    technicalSummary !== undefined &&
    technicalSummary !== null &&
    (typeof technicalSummary !== 'string' || technicalSummary.length > MAX_TECHNICAL_SUMMARY_LENGTH)
  ) {
    return null;
  }

  if (typeof environment !== 'string' || !ALLOWED_ENVIRONMENTS.has(environment)) {
    return null;
  }

  if (!isValidAttachment(attachment)) {
    return null;
  }

  return {
    reportId,
    category,
    summary: summary.trim(),
    description: description.trim(),
    appVersion,
    profile,
    technicalSummary: technicalSummary ?? null,
    environment,
    attachment: attachment ?? null,
  };
}
