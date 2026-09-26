using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CommunityPoke.UnityGit
{
    /// <summary>
    /// High-level git operations for one working tree. All methods are async and
    /// safe to fire from the editor main thread — work happens on a thread pool
    /// thread and results are returned via Task.
    /// </summary>
    public sealed class GitRepository
    {
        public string Root { get; private set; }

        public GitRepository(string root)
        {
            Root = root;
        }

        public static bool IsGitAvailable()
        {
            return GitRunner.Run(Environment.CurrentDirectory, "--version").Success;
        }

        /// <summary>Returns the toplevel of the repo containing <paramref name="dir"/>, or null.</summary>
        public static async Task<string> FindRootAsync(string dir)
        {
            var r = await GitRunner.RunAsync(dir, "rev-parse", "--show-toplevel").ConfigureAwait(false);
            return r.Success ? r.StdOut.Trim() : null;
        }

        public static async Task<GitRepository> OpenAsync(string dir)
        {
            var root = await FindRootAsync(dir).ConfigureAwait(false);
            return root == null ? null : new GitRepository(root);
        }

        public Task<GitResult> InitAsync()
        {
            return GitRunner.RunAsync(Root, "init");
        }

        public async Task<GitStatus> GetStatusAsync()
        {
            var r = await GitRunner.RunAsync(Root, "status", "--porcelain=v1", "-b", "-uall").ConfigureAwait(false);
            return r.Success ? GitStatusParser.ParseStatus(r.StdOut) : null;
        }

        public Task<GitResult> StageAsync(params string[] paths)
        {
            var args = new List<string> { "add", "--" };
            args.AddRange(paths);
            return GitRunner.RunAsync(Root, args.ToArray());
        }

        public Task<GitResult> StageAllAsync()
        {
            return GitRunner.RunAsync(Root, "add", "-A");
        }

        public Task<GitResult> UnstageAsync(params string[] paths)
        {
            // `git reset -- <path>` works even on an unborn HEAD.
            var args = new List<string> { "reset", "-q", "--" };
            args.AddRange(paths);
            return GitRunner.RunAsync(Root, args.ToArray());
        }

        public Task<GitResult> UnstageAllAsync()
        {
            return GitRunner.RunAsync(Root, "reset", "-q");
        }

        /// <summary>Commits staged changes; message is piped on stdin so quoting never matters.</summary>
        public Task<GitResult> CommitAsync(string message)
        {
            return GitRunner.RunWithStdInAsync(Root, message + "\n", "commit", "-F", "-");
        }

        public Task<GitResult> RevertFileAsync(string path)
        {
            return GitRunner.RunAsync(Root, "checkout", "--", path);
        }

        public async Task<List<GitBranchInfo>> GetBranchesAsync()
        {
            var r = await GitRunner.RunAsync(Root, "branch",
                "--format=%(HEAD)%1f%(refname:short)%1f%(upstream:short)%1f%(upstream:track)%1f%(subject)")
                .ConfigureAwait(false);
            return r.Success ? GitStatusParser.ParseBranches(r.StdOut) : new List<GitBranchInfo>();
        }

        public Task<GitResult> CreateBranchAsync(string name, bool checkout)
        {
            return checkout
                ? GitRunner.RunAsync(Root, "checkout", "-b", name)
                : GitRunner.RunAsync(Root, "branch", name);
        }

        public Task<GitResult> CheckoutBranchAsync(string name)
        {
            return GitRunner.RunAsync(Root, "checkout", name);
        }

        public Task<GitResult> DeleteBranchAsync(string name, bool force)
        {
            return GitRunner.RunAsync(Root, "branch", force ? "-D" : "-d", name);
        }

        public Task<GitResult> FetchAsync()
        {
            return GitRunner.RunAsync(Root, "fetch", "--prune");
        }

        public Task<GitResult> PullAsync()
        {
            return GitRunner.RunAsync(Root, "pull", "--ff-only");
        }

        /// <summary>Pushes HEAD and sets upstream to origin when none exists.</summary>
        public Task<GitResult> PushAsync()
        {
            return GitRunner.RunAsync(Root, "push", "-u", "origin", "HEAD");
        }

        public async Task<List<GitCommitInfo>> GetLogAsync(int maxCount = 200)
        {
            var r = await GitRunner.RunAsync(Root, "log",
                "-n", maxCount.ToString(),
                "--pretty=format:%H%x1f%h%x1f%an%x1f%ad%x1f%s",
                "--date=format:%Y-%m-%d %H:%M").ConfigureAwait(false);
            return r.Success ? GitStatusParser.ParseLog(r.StdOut) : new List<GitCommitInfo>();
        }

        public async Task<List<string>> GetCommitFilesAsync(string hash)
        {
            var r = await GitRunner.RunAsync(Root, "show", "--name-only", "--format=", hash)
                .ConfigureAwait(false);
            return r.Success ? GitStatusParser.ParseNameOnly(r.StdOut) : new List<string>();
        }

        /// <summary>Unified diff for one file against HEAD; falls back to worktree+index diffs on unborn HEAD.</summary>
        public async Task<GitResult> GetFileDiffAsync(string path)
        {
            var r = await GitRunner.RunAsync(Root, "diff", "HEAD", "--no-color", "--", path).ConfigureAwait(false);
            if (r.Success) return r;

            var worktree = await GitRunner.RunAsync(Root, "diff", "--no-color", "--", path).ConfigureAwait(false);
            var staged = await GitRunner.RunAsync(Root, "diff", "--cached", "--no-color", "--", path).ConfigureAwait(false);
            var combined = new GitResult();
            combined.StdOut = (worktree.StdOut ?? "") + (staged.StdOut ?? "");
            combined.ExitCode = worktree.Success || staged.Success ? 0 : worktree.ExitCode;
            combined.StdErr = worktree.Success ? staged.StdErr : worktree.StdErr;
            return combined;
        }

        public async Task<string> GetRemoteUrlAsync()
        {
            var r = await GitRunner.RunAsync(Root, "remote", "get-url", "origin").ConfigureAwait(false);
            return r.Success ? r.StdOut.Trim() : null;
        }

        public Task<GitResult> SetRemoteAsync(string url)
        {
            return GitRunner.RunAsync(Root, "remote", "add", "origin", url);
        }

        public async Task<GitResult> SetIdentityAsync(string name, string email)
        {
            var r = await SetLocalConfigAsync("user.name", name).ConfigureAwait(false);
            if (!r.Success) return r;
            return await SetLocalConfigAsync("user.email", email).ConfigureAwait(false);
        }

        public Task<GitResult> SetLocalConfigAsync(string key, string value)
        {
            return GitRunner.RunAsync(Root, "config", "--local", key, value);
        }
    }
}
