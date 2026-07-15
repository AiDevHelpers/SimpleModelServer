namespace SimpleModelServer.Common.Configuration;

/// <summary>
///     Centralized configuration path management for SimpleModelServer.
///     Handles ProgramData, environment variables, and fallback logic.
/// </summary>
public static class ConfigFileManager
{
    /// <summary>
    ///     Gets the configuration directory path.
    ///     Priority:
    ///     1. SIMPLEMODELSERVER_WORKSPACE env var + ".tokensaver" subfolder
    ///     2. ProgramData\SimpleModelServer
    ///     3. Fallback to exe directory if ProgramData unavailable
    /// </summary>
    public static string GetConfigDirectory()
    {
        string? workspace = Environment.GetEnvironmentVariable("SIMPLEMODELSERVER_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(workspace))
        {
            // ".local-helper" was the shared per-workspace data folder's old name (from before
            // the LocalHelper -> TokenSaver rename). Prefer the new ".tokensaver" name, but keep
            // reading from the old folder if a workspace still only has that one — avoids
            // silently losing an existing SimpleModelServer.config.json on upgrade.
            string dir = Path.Combine(workspace, ".tokensaver");
            string legacyDir = Path.Combine(workspace, ".local-helper");
            if (!Directory.Exists(dir) && Directory.Exists(legacyDir))
            {
                dir = legacyDir;
            }

            return dir;
        }

        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(programData))
        {
            return Path.Combine(programData, "SimpleModelServer");
        }

        // Fallback to exe directory
        string exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory)
                     ?? AppContext.BaseDirectory;
        return exeDir;
    }

    /// <summary>
    ///     Gets the full path to a configuration file in the config directory.
    /// </summary>
    public static string GetConfigFilePath(string filename)
    {
        return Path.Combine(GetConfigDirectory(), filename);
    }
}
