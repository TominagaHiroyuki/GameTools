/**
    @file GoogleAuthService.cs
    @brief Google Auth Service
*/

using Google.Apis.Auth.OAuth2;


namespace MasterDataConverter;

public static class GoogleAuthService
{
    private static ICredential _credential = null!;

    /// <summary>
    /// Get Credential
    /// </summary>
    /// <param name="keyPath">Key Path</param>
    /// <param name="scope">Scope</param>
    /// <returns></returns>
    public static ICredential GetCredential(string keyPath, string[] scope)
    {
        if (_credential != null)
        {
            return _credential;
        }

        using var stream = new FileStream(keyPath, FileMode.Open, FileAccess.Read);
        // key.json がサービスアカウントの場合は ServiceAccountCredential として読み込まれるため、
        // CredentialFactory.FromStream<GoogleCredential> では型不一致になる
        _credential = CredentialFactory.FromStream<ServiceAccountCredential>(stream);

        return _credential;
    }
}