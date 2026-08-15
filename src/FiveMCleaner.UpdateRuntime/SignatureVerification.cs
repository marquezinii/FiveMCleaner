using System.Security.Cryptography;

namespace FiveMCleaner.UpdateRuntime;

/// <summary>
/// Shared ECDSA check behind both signed manifests in the trust chain
/// (release and broker): decode the signature, import the embedded public
/// key, and verify the canonical payload. Kept in one place so the two
/// policies fail the same way on the same malformed input.
/// </summary>
internal static class SignatureVerification
{
    public static void Verify(
        byte[] canonicalPayload,
        ReadOnlySpan<byte> publicKey,
        string signatureBase64,
        string invalidSignatureMessage,
        string invalidPublicKeyMessage,
        string signatureMismatchMessage)
    {
        byte[] signature;
        try { signature = Convert.FromBase64String(signatureBase64); }
        catch (FormatException) { throw new InvalidDataException(invalidSignatureMessage); }
        using var verifier = ECDsa.Create();
        try
        {
            verifier.ImportSubjectPublicKeyInfo(publicKey, out var bytesRead);
            if (bytesRead != publicKey.Length) throw new CryptographicException("Chave pública contém dados extras.");
        }
        catch (CryptographicException) { throw new CryptographicException(invalidPublicKeyMessage); }
        if (!verifier.VerifyData(canonicalPayload, signature, HashAlgorithmName.SHA256))
            throw new CryptographicException(signatureMismatchMessage);
    }
}
