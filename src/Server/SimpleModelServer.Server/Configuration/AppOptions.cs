namespace SimpleModelServer.Server.Configuration;

internal sealed class AppOptions
{
    public bool Service { get; init; }
    public bool Router { get; init; }
    public bool Kill { get; init; }
    public bool Help { get; init; }
    public bool InstallService { get; init; }
    public bool UninstallService { get; init; }

    /// <summary>Null means use the default path resolved by DefaultConfig.LoaderConfigPath().</summary>
    public string? ConfigPath { get; init; }

    /// <summary>CLI override for router port — takes precedence over config file value.</summary>
    public int? RouterPortOverride { get; init; }

    public static AppOptions Parse(string[] args)
    {
        bool service = false;
        bool router = false;
        bool kill = false;
        bool help = false;
        bool installService = false;
        bool uninstallService = false;
        string? configPath = null;
        int? routerPortOverride = null;

        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            switch (arg.ToUpperInvariant())
            {
                case "--SERVICE":
                case "-SERVICE":
                    service = true;
                    break;
                case "--INSTALL-SERVICE":
                case "-INSTALL-SERVICE":
                    installService = true;
                    break;
                case "--UNINSTALL-SERVICE":
                case "-UNINSTALL-SERVICE":
                    uninstallService = true;
                    break;
                case "--ROUTER":
                case "-ROUTER":
                    router = true;
                    break;
                case "--KILL":
                case "-KILL":
                    kill = true;
                    break;
                case "--HELP":
                case "-HELP":
                case "-H":
                case "/?":
                    help = true;
                    break;
                case "--ROUTER-PORT":
                case "-ROUTER-PORT":
                    if (!int.TryParse(NextValue(args, ref index, arg), out int rp))
                    {
                        throw new ArgumentException("Router port must be an integer.");
                    }

                    routerPortOverride = rp;
                    break;
                case "--CONFIG":
                case "--CONFIG-FILE":
                case "-CONFIG":
                    configPath = NextValue(args, ref index, arg);
                    break;
            }
        }

        return new AppOptions
        {
            Service = service,
            Router = router,
            Kill = kill,
            Help = help,
            ConfigPath = configPath,
            RouterPortOverride = routerPortOverride,
            InstallService = installService,
            UninstallService = uninstallService
        };
    }

    private static string NextValue(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{name} requires a value.");
        }

        index++;
        return args[index];
    }
}
