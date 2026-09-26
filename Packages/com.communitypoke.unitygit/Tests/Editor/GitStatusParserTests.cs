using NUnit.Framework;

namespace CommunityPoke.UnityGit.Tests
{
    public class GitStatusParserTests
    {
        [Test]
        public void ParsesBranchHeader_WithUpstreamAndCounts()
        {
            var s = GitStatusParser.ParseStatus(
                "## main...origin/main [ahead 2, behind 1]\n");
            Assert.AreEqual("main", s.Branch);
            Assert.AreEqual("origin/main", s.Upstream);
            Assert.AreEqual(2, s.Ahead);
            Assert.AreEqual(1, s.Behind);
            Assert.IsFalse(s.IsDetached);
        }

        [Test]
        public void ParsesBranchHeader_NoCommitsYet()
        {
            var s = GitStatusParser.ParseStatus("## No commits yet on main\n?? a.txt\n");
            Assert.AreEqual("main", s.Branch);
            Assert.AreEqual(1, s.Changes.Count);
            Assert.IsTrue(s.Changes[0].IsUntracked);
        }

        [Test]
        public void ParsesDetachedHead()
        {
            var s = GitStatusParser.ParseStatus("## HEAD (no branch)\n");
            Assert.IsTrue(s.IsDetached);
        }

        [Test]
        public void ParsesMixedChanges()
        {
            var s = GitStatusParser.ParseStatus(
                "## main\nM  staged.cs\n M unstaged.cs\nMM both.cs\n?? new.cs\nUU conflict.cs\nR  old.cs -> renamed.cs\n");
            Assert.AreEqual(6, s.Changes.Count);

            Assert.IsTrue(s.Changes[0].HasStagedChange);
            Assert.IsFalse(s.Changes[0].HasUnstagedChange);

            Assert.IsTrue(s.Changes[1].HasUnstagedChange);
            Assert.IsFalse(s.Changes[1].HasStagedChange);

            Assert.IsTrue(s.Changes[2].HasStagedChange);
            Assert.IsTrue(s.Changes[2].HasUnstagedChange);

            Assert.IsTrue(s.Changes[4].IsUnmerged);

            Assert.AreEqual("old.cs", s.Changes[5].OriginalPath);
            Assert.AreEqual("renamed.cs", s.Changes[5].Path);
        }

        [Test]
        public void ParsesLog()
        {
            var commits = GitStatusParser.ParseLog(
                "abc123def456\u001fabc123\u001fAda\u001f2026-01-02 03:04\u001finitial commit\n");
            Assert.AreEqual(1, commits.Count);
            Assert.AreEqual("abc123def456", commits[0].Hash);
            Assert.AreEqual("initial commit", commits[0].Subject);
        }

        [Test]
        public void ParsesBranches()
        {
            var branches = GitStatusParser.ParseBranches(
                "*\u001fmain\u001forigin/main\u001f[ahead 1]\u001fwork in progress\n" +
                " \u001ffeature\u001f\u001f\u001fother work\n");
            Assert.AreEqual(2, branches.Count);
            Assert.IsTrue(branches[0].IsCurrent);
            Assert.AreEqual(1, branches[0].Ahead);
            Assert.AreEqual("feature", branches[1].Name);
        }

        [Test]
        public void UnquotesEscapedPath()
        {
            Assert.AreEqual("plain.cs", GitStatusParser.Unquote("\"plain.cs\""));
            Assert.AreEqual("sp ace.cs", GitStatusParser.Unquote("\"sp ace.cs\""));
        }
    }
}
