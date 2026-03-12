using Windows.Security.Credentials;

namespace FluentView.AI.SampleApp.Helpers;

public static class PasswordVaultHelper
{
    private const string ResourceName = "FluentView.AI.SampleApp";

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
