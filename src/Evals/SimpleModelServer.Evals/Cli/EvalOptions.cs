namespace SimpleModelServer.Evals.Cli;

public sealed class EvalOptions : IEvalOptions
{
    public required EvalMode Mode { get; init; }
    public string? RouterUrl { get; init; }
    public string? GenUrl { get; init; }
    public string Label { get; init; } = "unlabeled";
    public string CasesDir { get; init; } = "tests/golden";
    public string? Filter { get; init; }
    public int NPredict { get; init; } = 1800;
    public double Temperature { get; init; } = 0.2;
    public int TimeoutSeconds { get; init; } = 180;
    public string ReportsDir { get; init; } = "tests/golden/reports";

    public static EvalOptions Parse(string[] args)
    {
        EvalMode? mode = null;
        string? routerUrl = null;
        string? genUrl = null;
        string label = "unlabeled";
        string casesDir = "tests/golden";
        string? filter = null;
        int nPredict = 1800;
        double temperature = 0.2;
        int timeoutSeconds = 180;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string Next(string flag) => i + 1 < args.Length
                ? args[++i]
                : throw new ArgumentException($"Missing value for {flag}");

            switch (arg)
            {
                case "--mode":
                    mode = Next(arg) switch
                    {
                        "router" => EvalMode.Router,
                        "codegen" => EvalMode.Codegen,
                        "all" => EvalMode.All,
                        string other => throw new ArgumentException($"Unknown --mode value: {other}")
                    };
                    break;
                case "--router-url":
                    routerUrl = Next(arg);
                    break;
                case "--gen-url":
                    genUrl = Next(arg);
                    break;
                case "--label":
                    label = Next(arg);
                    break;
                case "--cases":
                    casesDir = Next(arg);
                    break;
                case "--filter":
                    filter = Next(arg);
                    break;
                case "--n-predict":
                    nPredict = int.Parse(Next(arg));
                    break;
                case "--temperature":
                    temperature = double.Parse(Next(arg));
                    break;
                case "--timeout":
                    timeoutSeconds = int.Parse(Next(arg));
                    break;
                case "--help" or "-h":
                    throw new HelpRequestedException();
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        if (mode is null)
        {
            throw new ArgumentException("--mode is required (router | codegen | all)");
        }

        if (mode is EvalMode.Router or EvalMode.All && string.IsNullOrWhiteSpace(routerUrl))
        {
            throw new ArgumentException("--router-url is required for --mode router/all");
        }

        if (mode is EvalMode.Codegen or EvalMode.All && string.IsNullOrWhiteSpace(genUrl))
        {
            throw new ArgumentException("--gen-url is required for --mode codegen/all");
        }

        return new EvalOptions
        {
            Mode = mode.Value,
            RouterUrl = routerUrl,
            GenUrl = genUrl,
            Label = label,
            CasesDir = casesDir,
            Filter = filter,
            NPredict = nPredict,
            Temperature = temperature,
            TimeoutSeconds = timeoutSeconds
        };
    }
}

public sealed class HelpRequestedException : Exception;
