using System.Diagnostics;

namespace SimpleModelServer.Evals.Reporting;

public static class GitInfo
{
    public static string GetCurrentSha(string repoDir)
    {
        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo("git", "rev-parse HEAD")
                {
                    WorkingDirectory = repoDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return process.ExitCode == 0 && output.Length > 0 ? output : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
