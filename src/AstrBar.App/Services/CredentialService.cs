using System.Security.Cryptography;
using System.Text;

namespace AstrBar.Services;

public sealed class CredentialService
{
    private readonly string _directory;
    private readonly string _protocolTokenPath;
    private readonly string _legacyApiKeyPath;
    private readonly string _sshPasswordPath;

    public CredentialService()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AstrBar");
        _protocolTokenPath = Path.Combine(_directory, "protocol-token.bin");
        _legacyApiKeyPath = Path.Combine(_directory, "api-key.bin");
        _sshPasswordPath = Path.Combine(_directory, "ssh-password.bin");
    }

    public string LoadProtocolToken()
    {
        var token = LoadProtected(_protocolTokenPath);
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        // v0.3.x migration: reuse the old protected API key once, but save future
        // values under the protocol-specific filename.
        return LoadProtected(_legacyApiKeyPath);
    }

    public void SaveProtocolToken(string token) =>
        SaveProtected(_protocolTokenPath, token);

    // Compatibility aliases for any out-of-tree code built against v0.3.x.
    public string LoadApiKey() => LoadProtocolToken();
    public void SaveApiKey(string apiKey) => SaveProtocolToken(apiKey);

    public string LoadSshPassword() => LoadProtected(_sshPasswordPath);
    public void SaveSshPassword(string password) => SaveProtected(_sshPasswordPath, password);

    private static string LoadProtected(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            var protectedBytes = File.ReadAllBytes(path);
            var plainBytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private void SaveProtected(string path, string value)
    {
        Directory.CreateDirectory(_directory);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            return;
        }

        var plainBytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(
            plainBytes,
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, protectedBytes);
    }
}
