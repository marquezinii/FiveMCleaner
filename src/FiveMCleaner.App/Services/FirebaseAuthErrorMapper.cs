namespace FiveMCleaner.App.Services;

internal static class FirebaseAuthErrorMapper
{
    public static string Map(string? code, bool sensitiveFlow = false) => code switch
    {
        "TOO_MANY_ATTEMPTS_TRY_LATER" => "Muitas tentativas. Aguarde alguns minutos antes de tentar novamente.",
        "NETWORK_REQUEST_FAILED" => "Não foi possível conectar. Verifique a internet e tente novamente.",
        "INVALID_ID_TOKEN" or "TOKEN_EXPIRED" or "USER_NOT_FOUND" or "INVALID_REFRESH_TOKEN" or "USER_DISABLED" => "Sua sessão não é mais válida. Entre novamente.",
        "CREDENTIAL_TOO_OLD_LOGIN_AGAIN" or "REQUIRES_RECENT_LOGIN" => "Confirme sua senha para concluir esta alteração.",
        "EMAIL_EXISTS" => "Este e-mail já está em uso. Se a conta for sua, use 'Esqueci minha senha' para recuperar o acesso.",
        "WEAK_PASSWORD" or "PASSWORD_DOES_NOT_MEET_REQUIREMENTS" => "A senha não atende à política de segurança.",
        "INVALID_EMAIL" => "Informe um endereço de e-mail válido.",
        _ when sensitiveFlow => "Se os dados estiverem corretos, você receberá as instruções necessárias.",
        _ => "Não foi possível concluir agora. Confira os dados e tente novamente."
    };
}
