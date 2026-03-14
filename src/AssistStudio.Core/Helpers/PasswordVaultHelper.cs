using Windows.Security.Credentials;

namespace FieldCure.AssistStudio.Helpers;

/// <summary>
/// Provides methods for securely storing and retrieving API keys using the Windows Credential Manager (PasswordVault).
/// </summary>
public static class PasswordVaultHelper
{
    private const string ResourceName = "FieldCure.AssistStudio";

    /// <summary>
    /// Saves an API key for the specified preset name, replacing any existing entry.
    /// </summary>
    /// <param name="presetName">The preset name used as the credential username.</param>
    /// <param name="apiKey">The API key to store. If empty, any existing entry is removed.</param>
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
    /// Loads the API key for the specified preset name from the credential store.
    /// </summary>
    /// <param name="presetName">The preset name used as the credential username.</param>
    /// <returns>The stored API key, or an empty string if not found.</returns>
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
    /// <param name="presetName">The preset name whose API key should be removed.</param>
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
}
