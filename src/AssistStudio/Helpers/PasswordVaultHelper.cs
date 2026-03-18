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
}
