internal sealed class ChatOptions
{
    public bool Help { get; init; }

    /// <summary>Null means use the default path resolved by DefaultConfig.ChatConfigPath().</summary>
    public string? ConfigPath { get; init; }

    public static ChatOptions Parse(string[] args)
    {
        bool help = false;
        string? configPath = null;

        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            switch (arg.ToUpperInvariant())
            {
                case "--HELP":
                case "-HELP":
                case "-H":
                case "/?":
                    help = true;
                    break;
                case "--CONFIG":
                case "--CONFIG-FILE":
                case "-CONFIG":
                    configPath = NextValue(args, ref index, arg);
                    break;
            }
        }

        return new ChatOptions { Help = help, ConfigPath = configPath };
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
