using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace CommunityPoke.UnityGit
{
    /// <summary>
    /// Result of a single git invocation. Never throws; check <see cref="Success"/>.
    /// </summary>
    public sealed class GitResult
    {
        public int ExitCode;
        public string StdOut = "";
        public string StdErr = "";
        public string Arguments = "";

        public bool Success => ExitCode == 0;

        public string CombinedOutput
        {
            get
            {
                var sb = new StringBuilder();
                if (!string.IsNullOrEmpty(StdOut)) sb.Append(StdOut.TrimEnd());
                if (!string.IsNullOrEmpty(StdErr))
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(StdErr.TrimEnd());
                }
                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// Thin async wrapper around the git CLI. No UnityEngine dependencies so the
    /// whole git layer can be unit-tested outside the editor.
    /// </summary>
    public static class GitRunner
    {
        /// <summary>Path to the git binary. Defaults to "git" (resolved via PATH).</summary>
        public static string GitPath = "git";

        public static Task<GitResult> RunAsync(string workingDirectory, params string[] args)
        {
            return Task.Run(() => Run(workingDirectory, null, args));
        }

        /// <summary>Distinct name so a plain string arg can never mis-bind to stdin.</summary>
        public static Task<GitResult> RunWithStdInAsync(string workingDirectory, string stdIn, params string[] args)
        {
            return Task.Run(() => Run(workingDirectory, stdIn, args));
        }

        public static GitResult Run(string workingDirectory, params string[] args)
        {
            return Run(workingDirectory, null, args);
        }

        static GitResult Run(string workingDirectory, string stdIn, string[] args)
        {
            var result = new GitResult { Arguments = string.Join(" ", args) };
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = GitPath,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = stdIn != null,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                foreach (var arg in args)
                    psi.ArgumentList.Add(arg);

                // Never block on a terminal credential prompt or open an editor.
                psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
                psi.Environment["GIT_EDITOR"] = "true";

                using (var process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        result.ExitCode = -1;
                        result.StdErr = "Failed to start git process.";
                        return result;
                    }

                    // Begin draining stderr asynchronously so a noisy child cannot
                    // deadlock on a full pipe while we write stdin.
                    var stderrTask = process.StandardError.ReadToEndAsync();

                    if (stdIn != null)
                    {
                        process.StandardInput.Write(stdIn);
                        process.StandardInput.Close();
                    }

                    result.StdOut = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    result.StdErr = stderrTask.Result;
                    result.ExitCode = process.ExitCode;
                }
            }
            catch (Exception e)
            {
                result.ExitCode = -1;
                result.StdErr = e.Message;
            }
            return result;
        }
    }
}
