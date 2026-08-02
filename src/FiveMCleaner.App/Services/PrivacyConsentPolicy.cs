namespace FiveMCleaner.App.Services;

/// <summary>
/// One versioned entry in the privacy consent history: the version number and
/// a short, user-facing description of what changed relative to the previous
/// version. Purely descriptive data — no UI, network, or storage dependency.
/// </summary>
public sealed record PrivacyConsentVersionEntry(int Version, string Summary);

/// <summary>
/// Central, dependency-free source of truth for the privacy consent version.
/// Determines only "which version is current" and "what changed in each
/// version" — it has no knowledge of UI, disk, Cloudflare, or Sentry, and must
/// stay that way so it can be unit tested and reused by both the consent
/// screen and the settings evaluator without pulling in those concerns.
/// </summary>
public static class PrivacyConsentPolicy
{
    /// <summary>
    /// The current privacy consent version. Bump this whenever a material
    /// change happens to what is collected or where it is sent (for example,
    /// adding crash reporting, or switching the telemetry transport from
    /// FormSubmit to another destination) — every user, including those who
    /// already accepted an older version, will be asked to confirm again.
    /// </summary>
    public const int CurrentVersion = 4;

    /// <summary>
    /// Full history of consent versions, oldest first, each with a short
    /// description of what changed. Version 1 is the baseline that
    /// introduces separate, explicit consent for anonymous telemetry and
    /// crash reports. Version 2 adds a perfil de hardware (CPU/GPU/RAM) e
    /// os identificadores de ação aplicada ao evento de telemetria de uso —
    /// mudança material que exige renovar o consentimento de todos, mesmo
    /// quem já tinha aceitado a versão 1.
    /// </summary>
    public static readonly IReadOnlyList<PrivacyConsentVersionEntry> History =
    [
        new PrivacyConsentVersionEntry(
            1,
            "Introduz consentimento explícito e separado para telemetria de uso e relatórios de falhas (crash reports), " +
            "ambos apresentados numa tela dedicada antes de qualquer envio."),
        new PrivacyConsentVersionEntry(
            2,
            "A telemetria de uso passa a incluir um perfil de hardware (modelo de CPU/GPU e faixa de RAM, os mesmos já " +
            "mostrados no diagnóstico local do app) e os identificadores técnicos das ações aplicadas numa otimização, " +
            "para estatísticas agregadas (por exemplo, hardware ou funcionalidade mais comuns). Continua sem qualquer " +
            "identificador único de máquina, caminho ou texto livre.")
        ,
        new PrivacyConsentVersionEntry(
            3,
            "Adiciona eventos estruturados de falha e recuperação do atualizador (etapa, resultado, código e versões), " +
            "sem caminhos, logs completos, texto livre ou identificador de máquina."),
        new PrivacyConsentVersionEntry(
            4,
            "Separa diagnósticos essenciais de dados opcionais: estabilidade, atualizações e o resultado técnico das " +
            "operações permanecem ativos; hardware, perfil e ações usadas só são compartilhados com a opção ativada.")
    ];

    /// <summary>
    /// True when a stored consent version is missing or older than
    /// <see cref="CurrentVersion"/>, meaning the user must be asked again
    /// before anything is sent.
    /// </summary>
    public static bool RequiresRenewal(int? storedVersion) =>
        storedVersion is null || storedVersion.Value < CurrentVersion;
}
