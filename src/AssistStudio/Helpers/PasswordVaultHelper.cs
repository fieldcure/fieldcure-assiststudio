using System.Runtime.InteropServices;
using System.Text;
using Windows.Security.Credentials;

namespace AssistStudio.Helpers;

/// <summary>
/// Provides methods for securely storing and retrieving API keys using the Windows Credential Manager (PasswordVault).
/// </summary>
internal static class PasswordVaultHelper
{
    #region Constants

    /// <summary>The resource name used to identify credentials in the Windows PasswordVault.</summary>
    private const string ResourceName = "FieldCure.AssistStudio";

    #endregion

    #region Public Methods

    /// <summary>
    /// Saves an API key for the specified preset name, replacing any existing entry.
    /// </summary>
    public static void SaveApiKey(string presetName, string apiKey)
    {
        var vault = new PasswordVault();

        // Remove existing entry if any (avoid Retrieve exception by checking first)
        var existing = FindCredential(vault, presetName);
        if (existing is not null)
        {
            vault.Remove(existing);
        }

        if (!string.IsNullOrEmpty(apiKey))
        {
            vault.Add(new PasswordCredential(ResourceName, presetName, apiKey));
        }
    }

    /// <summary>
    /// Checks whether an API key exists for the specified preset name without retrieving the password.
    /// </summary>
    public static bool HasApiKey(string presetName)
    {
        var vault = new PasswordVault();
        return FindCredential(vault, presetName) is not null;
    }

    /// <summary>
    /// Loads the API key for the specified preset name from the credential store.
    /// Returns empty string if not found.
    /// </summary>
    public static string LoadApiKey(string presetName)
    {
        var vault = new PasswordVault();
        var credential = FindCredential(vault, presetName);
        if (credential is null) return "";

        credential.RetrievePassword();
        return credential.Password;
    }

    /// <summary>
    /// Deletes the stored API key for the specified preset name from the credential store.
    /// </summary>
    public static void DeleteApiKey(string presetName)
    {
        var vault = new PasswordVault();
        var credential = FindCredential(vault, presetName);
        if (credential is not null)
        {
            vault.Remove(credential);
        }
    }

    #endregion

    #region MCP Environment Variable Methods

    /// <summary>
    /// Saves an MCP server environment variable value to the vault.
    /// Stored under <c>McpEnv_{serverId}_{key}</c>.
    /// </summary>
    public static void SaveMcpEnvVar(string serverId, string key, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            DeleteMcpEnvVar(serverId, key);
            return;
        }

        SaveApiKey($"McpEnv_{serverId}_{key}", value);
    }

    /// <summary>
    /// Loads an MCP server environment variable value from the vault.
    /// </summary>
    public static string LoadMcpEnvVar(string serverId, string key)
    {
        return LoadApiKey($"McpEnv_{serverId}_{key}");
    }

    /// <summary>
    /// Deletes an MCP server environment variable from the vault.
    /// </summary>
    public static void DeleteMcpEnvVar(string serverId, string key)
    {
        DeleteApiKey($"McpEnv_{serverId}_{key}");
    }

    /// <summary>
    /// Deletes all MCP environment variables for a given server.
    /// </summary>
    public static void DeleteAllMcpEnvVars(string serverId, IEnumerable<string>? keys)
    {
        if (keys is null) return;
        foreach (var key in keys)
            DeleteMcpEnvVar(serverId, key);
    }

    /// <summary>
    /// Saves multiple MCP environment variables to the vault.
    /// </summary>
    public static void SaveMcpEnvVars(string serverId, Dictionary<string, string> envVars)
    {
        foreach (var (key, value) in envVars)
            SaveMcpEnvVar(serverId, key, value);
    }

    /// <summary>
    /// Loads multiple MCP environment variables from the vault.
    /// </summary>
    public static Dictionary<string, string> LoadMcpEnvVars(string serverId, IEnumerable<string> keys)
    {
        var result = new Dictionary<string, string>();
        foreach (var key in keys)
        {
            var value = LoadMcpEnvVar(serverId, key);
            if (!string.IsNullOrEmpty(value))
                result[key] = value;
        }
        return result;
    }

    #endregion

    #region Credential Manager Sync

    /// <summary>
    /// Copies all API keys from UWP PasswordVault to Win32 Credential Manager
    /// so that external processes (e.g., Runner) can access them via CredEnumerate.
    /// Packaged app PasswordVault is isolated; this bridges the gap.
    /// </summary>
    public static void SyncToCredentialManager()
    {
        IReadOnlyList<PasswordCredential> credentials;
        try
        {
            var vault = new PasswordVault();
            credentials = vault.FindAllByResource(ResourceName);
        }
        catch
        {
            return;
        }

        foreach (var cred in credentials)
        {
            try
            {
                cred.RetrievePassword();
                if (!string.IsNullOrEmpty(cred.Password))
                    CredManagerWrite(cred.UserName, cred.Password);
            }
            catch
            {
                // Skip individual failures
            }
        }
    }

    /// <summary>
    /// Writes a single credential to Win32 Credential Manager with the same
    /// Resource/UserName pattern that Runner's CredentialService reads via CredEnumerate.
    /// </summary>
    private static void CredManagerWrite(string userName, string secret)
    {
        var targetName = $"{ResourceName}/{userName}";
        var secretBytes = Encoding.Unicode.GetBytes(secret);

        var credential = new CREDENTIAL
        {
            Type = 1, // CRED_TYPE_GENERIC
            TargetName = targetName,
            UserName = userName,
            CredentialBlobSize = (uint)secretBytes.Length,
            CredentialBlob = Marshal.AllocHGlobal(secretBytes.Length),
            Persist = 2, // CRED_PERSIST_LOCAL_MACHINE
        };

        try
        {
            Marshal.Copy(secretBytes, 0, credential.CredentialBlob, secretBytes.Length);
            CredWrite(ref credential, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(credential.CredentialBlob);
        }
    }

#pragma warning disable SYSLIB1054
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);
#pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Finds a credential by preset name without throwing if not found.
    /// Uses <see cref="PasswordVault.FindAllByResource"/> to avoid first-chance exceptions
    /// that occur with <see cref="PasswordVault.Retrieve"/> when the entry does not exist.
    /// </summary>
    private static PasswordCredential? FindCredential(PasswordVault vault, string presetName)
    {
        try
        {
            var results = vault.FindAllByResource(ResourceName);
            return results.FirstOrDefault(c => c.UserName == presetName);
        }
        catch
        {
            // FindAllByResource throws if no entries exist for the resource at all.
            return null;
        }
    }

    #endregion
}
