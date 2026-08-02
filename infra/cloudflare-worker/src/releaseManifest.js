// Pure, dependency-free validation for the signed release feed served at
// /update/manifest. Kept separate from index.js so the closed schema can be
// unit tested without a Worker request. Returns the parsed manifest, or
// `null` when the payload does not match the schema -- never throws.

export const MAX_MANIFEST_BYTES = 65_536;

const RELEASE_DOWNLOAD_PREFIX = /^https:\/\/github\.com\/marquezinii\/FiveMCleaner\/releases\/download\//;
const SEMVER = /^\d+\.\d+\.\d+$/;
const SHA256_HEX = /^[a-f0-9]{64}$/i;

const ALLOWED_KEYS = [
  'channel',
  'version',
  'minimumAllowedVersion',
  'packageUrl',
  'packageSha256',
  'packageSizeBytes',
  'signatureBase64',
];

/**
 * Validates the release-manifest JSON string. Returns the parsed manifest on
 * success, or `null` for any of: oversized payload, invalid JSON, unknown
 * keys, a non-stable channel, or a malformed version/URL/signature field.
 */
export function parseReleaseManifest(manifest) {
  if (typeof manifest !== 'string' || manifest.length === 0 || manifest.length > MAX_MANIFEST_BYTES) {
    return null;
  }

  let parsed;
  try {
    parsed = JSON.parse(manifest);
  } catch {
    return null;
  }

  const keys = parsed && typeof parsed === 'object' && !Array.isArray(parsed)
    ? Object.keys(parsed)
    : [];
  if (keys.length !== ALLOWED_KEYS.length || keys.some((key) => !ALLOWED_KEYS.includes(key))
      || parsed.channel !== 'stable'
      || !SEMVER.test(parsed.version)
      || !SEMVER.test(parsed.minimumAllowedVersion)
      || typeof parsed.packageUrl !== 'string'
      || !RELEASE_DOWNLOAD_PREFIX.test(parsed.packageUrl)
      || !SHA256_HEX.test(parsed.packageSha256)
      || !Number.isSafeInteger(parsed.packageSizeBytes) || parsed.packageSizeBytes <= 0
      || typeof parsed.signatureBase64 !== 'string'
      || parsed.signatureBase64.length < 80 || parsed.signatureBase64.length > 256) {
    return null;
  }

  return parsed;
}
