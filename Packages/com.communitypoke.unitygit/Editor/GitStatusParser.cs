using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CommunityPoke.UnityGit
{
    /// <summary>
    /// Parsers for `git status --porcelain=v1 -b`, `git branch --format=...`
    /// and `git log` output. Pure string processing, no Unity dependencies.
    /// </summary>
    public static class GitStatusParser
    {
        static readonly Regex AheadBehindRegex =
            new Regex(@"\[ahead (\d+)(?:, behind (\d+))?\]|\[behind (\d+)\]", RegexOptions.Compiled);

        /// <summary>Parses `git status --porcelain=v1 -b` output.</summary>
        public static GitStatus ParseStatus(string porcelain)
        {
            var status = new GitStatus();
            if (string.IsNullOrEmpty(porcelain)) return status;

            var lines = porcelain.Split('\n');
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;

                if (line.StartsWith("## "))
                {
                    ParseBranchHeader(line.Substring(3), status);
                    continue;
                }
                if (line.StartsWith("#")) continue;
                if (line.Length < 4) continue;

                var change = new GitFileChange { X = line[0], Y = line[1] };
                var path = line.Substring(3);

                // Renames/copies in v1 are written as "orig -> new".
                var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrow >= 0 && (change.X == 'R' || change.X == 'C' || change.Y == 'R' || change.Y == 'C'))
                {
                    change.OriginalPath = Unquote(path.Substring(0, arrow));
                    change.Path = Unquote(path.Substring(arrow + 4));
                }
                else
                {
                    change.Path = Unquote(path);
                }

                if (!change.IsIgnored)
                    status.Changes.Add(change);
            }
            return status;
        }

        static void ParseBranchHeader(string header, GitStatus status)
        {
            // e.g. "main...origin/main [ahead 1, behind 2]", "HEAD (no branch)", "No commits yet on main"
            var ahead = AheadBehindRegex.Match(header);
            if (ahead.Success)
            {
                if (ahead.Groups[1].Success) status.Ahead = int.Parse(ahead.Groups[1].Value);
                var behind = ahead.Groups[2].Success ? ahead.Groups[2].Value
                            : ahead.Groups[3].Success ? ahead.Groups[3].Value : null;
                if (behind != null) status.Behind = int.Parse(behind);
            }

            var noCommits = header.StartsWith("No commits yet on ");
            if (noCommits) header = header.Substring("No commits yet on ".Length);
            if (header.StartsWith("HEAD"))
            {
                status.IsDetached = true;
                status.Branch = "HEAD (detached)";
                return;
            }

            var dots = header.IndexOf("...", StringComparison.Ordinal);
            if (dots >= 0)
            {
                status.Branch = header.Substring(0, dots).Trim();
                var rest = header.Substring(dots + 3);
                var bracket = rest.IndexOf(" [", StringComparison.Ordinal);
                status.Upstream = (bracket >= 0 ? rest.Substring(0, bracket) : rest).Trim();
            }
            else
            {
                var bracket = header.IndexOf(" [", StringComparison.Ordinal);
                status.Branch = (bracket >= 0 ? header.Substring(0, bracket) : header).Trim();
            }
        }

        /// <summary>
        /// Parses `git log --pretty=format:%H%x1f%h%x1f%an%x1f%ad%x1f%s` output
        /// (fields separated by ASCII unit-separator 0x1f).
        /// </summary>
        public static List<GitCommitInfo> ParseLog(string log)
        {
            var commits = new List<GitCommitInfo>();
            if (string.IsNullOrEmpty(log)) return commits;
            foreach (var raw in log.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                var f = line.Split('\u001f');
                if (f.Length < 5) continue;
                commits.Add(new GitCommitInfo
                {
                    Hash = f[0],
                    AbbrevHash = f[1],
                    Author = f[2],
                    Date = f[3],
                    Subject = f[4],
                });
            }
            return commits;
        }

        /// <summary>
        /// Parses `git branch --format=%(HEAD)%1f%(refname:short)%1f%(upstream:short)%1f%(upstream:track)%1f%(subject)`.
        /// </summary>
        public static List<GitBranchInfo> ParseBranches(string output)
        {
            var branches = new List<GitBranchInfo>();
            if (string.IsNullOrEmpty(output)) return branches;
            foreach (var raw in output.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                var f = line.Split('\u001f');
                var b = new GitBranchInfo
                {
                    IsCurrent = f.Length > 0 && f[0].Trim() == "*",
                    Name = f.Length > 1 ? f[1] : "",
                    Upstream = f.Length > 2 ? f[2] : "",
                    Subject = f.Length > 4 ? f[4] : "",
                };
                if (f.Length > 3)
                {
                    var m = AheadBehindRegex.Match(f[3]);
                    if (m.Success)
                    {
                        if (m.Groups[1].Success) b.Ahead = int.Parse(m.Groups[1].Value);
                        var behind = m.Groups[2].Success ? m.Groups[2].Value
                                    : m.Groups[3].Success ? m.Groups[3].Value : null;
                        if (behind != null) b.Behind = int.Parse(behind);
                    }
                }
                branches.Add(b);
            }
            return branches;
        }

        /// <summary>Parses `git show --name-only --format= <sha>` output into a file list.</summary>
        public static List<string> ParseNameOnly(string output)
        {
            var files = new List<string>();
            if (string.IsNullOrEmpty(output)) return files;
            foreach (var raw in output.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                files.Add(line);
            }
            return files;
        }

        /// <summary>Strips the C-style quoting git applies to unusual paths.</summary>
        public static string Unquote(string path)
        {
            path = path.Trim();
            if (path.Length >= 2 && path[0] == '"' && path[path.Length - 1] == '"')
            {
                // Git C-quotes unusual paths; octal escapes are raw UTF-8 bytes,
                // so accumulate bytes and decode once.
                var inner = path.Substring(1, path.Length - 2);
                var bytes = new List<byte>(inner.Length);
                for (var i = 0; i < inner.Length; i++)
                {
                    if (inner[i] == '\\' && i + 1 < inner.Length)
                    {
                        var next = inner[i + 1];
                        if (next == 'n') { bytes.Add((byte)'\n'); i++; continue; }
                        if (next == 't') { bytes.Add((byte)'\t'); i++; continue; }
                        if (next == '\\' || next == '"') { bytes.Add((byte)next); i++; continue; }
                        if (next >= '0' && next <= '7')
                        {
                            var j = i + 1; var val = 0; var digits = 0;
                            while (j < inner.Length && inner[j] >= '0' && inner[j] <= '7' && digits < 3)
                            { val = val * 8 + (inner[j] - '0'); j++; digits++; }
                            if (digits > 0) { bytes.Add((byte)val); i = j - 1; continue; }
                        }
                    }
                    bytes.Add((byte)inner[i]);
                }
                return Encoding.UTF8.GetString(bytes.ToArray());
            }
            return path;
        }
    }
}
