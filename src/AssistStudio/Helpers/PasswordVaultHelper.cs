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

        // Remove existing entry if any
        try
        {
            var existing = vault.Retrieve(ResourceName, presetName);
            vault.Remove(existing);
        }
        catch
        {
            // Not found — OK
        }

        if (!string.IsNullOrEmpty(apiKey))
        {
            vault.Add(new PasswordCredential(ResourceName, presetName, apiKey));
        }
    }

    /// <summary>
    /// Checks whether an API key exists for the specified preset name without retrieving the password.
    /// Uses FindAllByResource which avoids the heavier Retrieve call.
    /// </summary>
    public static bool HasApiKey(string presetName)
    {
        try
        {
            var vault = new PasswordVault();
            var results = vault.FindAllByResource(ResourceName);
            return results.Any(c => c.UserName == presetName);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Loads the API key for the specified preset name from the credential store.
    /// </summary>
    public static string LoadApiKey(string presetName)
    {
        try
        {
            var vault = new PasswordVault();
            var credential = vault.Retrieve(ResourceName, presetName);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Deletes the stored API key for the specified preset name from the credential store.
    /// </summary>
    public static void DeleteApiKey(string presetName)
    {
        try
        {
            var vault = new PasswordVault();
            var credential = vault.Retrieve(ResourceName, presetName);
            vault.Remove(credential);
        }
        catch
        {
            // Not found — OK
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
}
