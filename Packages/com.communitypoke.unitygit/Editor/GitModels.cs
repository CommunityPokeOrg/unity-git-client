using System.Collections.Generic;

namespace CommunityPoke.UnityGit
{
    public enum GitChangeKind
    {
        Staged,
        Unstaged,
        Untracked,
        Unmerged,
    }

    /// <summary>One entry from `git status --porcelain=v1`.</summary>
    public sealed class GitFileChange
    {
        /// <summary>Index (staged) status column.</summary>
        public char X;
        /// <summary>Worktree (unstaged) status column.</summary>
        public char Y;
        public string Path = "";
        /// <summary>Original path for renames/copies.</summary>
        public string OriginalPath;

        public bool IsUntracked => X == '?' && Y == '?';
        public bool IsIgnored => X == '!' && Y == '!';

        public bool IsUnmerged =>
            X == 'U' || Y == 'U' ||
            (X == 'A' && Y == 'A') ||
            (X == 'D' && Y == 'D');

        public bool HasStagedChange => !IsUntracked && !IsIgnored && !IsUnmerged && X != ' ' && X != '?';
        public bool HasUnstagedChange => IsUntracked || (!IsIgnored && !IsUnmerged && Y != ' ' && Y != '?');

        public string StatusLabel
        {
            get
            {
                if (IsUnmerged) return "conflict";
                if (IsUntracked) return "untracked";
                switch (X != ' ' ? X : Y)
                {
                    case 'M': return "modified";
                    case 'A': return "added";
                    case 'D': return "deleted";
                    case 'R': return "renamed";
                    case 'C': return "copied";
                    case 'T': return "type changed";
                    default: return (X.ToString() + Y).Trim();
                }
            }
        }
    }

    public sealed class GitStatus
    {
        /// <summary>Current branch name, or short hash when detached.</summary>
        public string Branch = "";
        public string Upstream = "";
        public bool IsDetached;
        public int Ahead;
        public int Behind;
        public readonly List<GitFileChange> Changes = new List<GitFileChange>();

        public bool HasChanges => Changes.Count > 0;
        public int StagedCount
        {
            get
            {
                var n = 0;
                foreach (var c in Changes) if (c.HasStagedChange) n++;
                return n;
            }
        }
    }

    public sealed class GitBranchInfo
    {
        public string Name = "";
        public string Upstream = "";
        public bool IsCurrent;
        public int Ahead;
        public int Behind;
        public string Subject = "";
    }

    public sealed class GitCommitInfo
    {
        public string Hash = "";
        public string AbbrevHash = "";
        public string Author = "";
        public string Date = "";
        public string Subject = "";
        /// <summary>Files touched by the commit; populated lazily on expand.</summary>
        public List<string> Files;
    }
}
