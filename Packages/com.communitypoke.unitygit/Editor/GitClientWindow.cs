using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace CommunityPoke.UnityGit.Editor
{
    /// <summary>
    /// In-editor Git client: status, staging, commit, branches, push/pull and
    /// history. Open via Window &gt; Git Client.
    /// </summary>
    public sealed class GitClientWindow : EditorWindow
    {
        enum Tab { Changes, Branches, History }

        static readonly string[] TabNames = { "Changes", "Branches", "History" };
        const int HistoryLimit = 200;

        [MenuItem("Window/Git Client")]
        [MenuItem("Tools/Git Client")]
        public static void Open()
        {
            var w = GetWindow<GitClientWindow>();
            w.titleContent = new GUIContent("Git");
            w.minSize = new Vector2(560, 320);
            w.Show();
        }

        // --- state -----------------------------------------------------------

        GitRepository _repo;
        string _repoPathInput;
        string _repoRoot;
        string _remoteUrl;

        GitStatus _status;
        List<GitBranchInfo> _branches = new List<GitBranchInfo>();
        List<GitCommitInfo> _commits = new List<GitCommitInfo>();
        readonly HashSet<string> _expandedCommits = new HashSet<string>();

        int _tab;
        Vector2 _changesScroll, _branchScroll, _historyScroll, _outputScroll, _diffScroll;
        string _commitMessage = "";
        string _newBranchName = "";
        bool _createAndSwitch = true;

        string _selectedPath;
        string _diffText;
        bool _showDiff;

        bool _showSettings;
        string _gitPathInput;
        string _identityName = "";
        string _identityEmail = "";
        string _newRemoteUrl = "";

        bool _busy;
        bool _gitAvailable = true;
        string _banner = "";
        MessageType _bannerType = MessageType.None;

        readonly Queue<Action> _mainQueue = new Queue<Action>();
        readonly object _queueLock = new object();

        // --- lifecycle ---------------------------------------------------------

        void OnEnable()
        {
            wantsConstantRepaint = false;
            EditorApplication.update += DrainMainQueue;
            if (string.IsNullOrEmpty(_repoPathInput))
                _repoPathInput = DefaultRepoPath();
            _gitPathInput = GitRunner.GitPath;
            Refresh();
        }

        void OnDisable()
        {
            EditorApplication.update -= DrainMainQueue;
        }

        void OnFocus()
        {
            if (!_busy) Refresh();
        }

        static string DefaultRepoPath()
        {
            try { return Directory.GetParent(Application.dataPath).FullName; }
            catch { return Environment.CurrentDirectory; }
        }

        // --- async plumbing ------------------------------------------------------
        // Git work runs on thread-pool threads; results are marshalled back to the
        // editor main thread through _mainQueue drained by EditorApplication.update.

        void Enqueue(Action a)
        {
            lock (_queueLock) _mainQueue.Enqueue(a);
        }

        void DrainMainQueue()
        {
            var ran = false;
            while (true)
            {
                Action a;
                lock (_queueLock)
                {
                    if (_mainQueue.Count == 0) break;
                    a = _mainQueue.Dequeue();
                }
                try { a(); }
                catch (Exception e) { Debug.LogException(e); }
                ran = true;
            }
            if (ran) Repaint();
        }

        void RunOp<T>(Func<Task<T>> work, Action<T> onDone)
        {
            _busy = true;
            work().ContinueWith(t =>
            {
                var result = t.Status == TaskStatus.RanToCompletion ? t.Result : default(T);
                var fault = t.Status == TaskStatus.Faulted ? t.Exception : null;
                Enqueue(() =>
                {
                    _busy = false;
                    if (fault != null) SetBanner("git " + fault.GetBaseException().Message, MessageType.Error);
                    else onDone(result);
                });
            });
        }

        void RunGit(string label, Func<Task<GitResult>> op, bool refreshAfter = true)
        {
            SetBanner(label + "…", MessageType.None);
            RunOp(op, r =>
            {
                var ok = r != null && r.Success;
                SetBanner(ok ? label + " — done" : label + " failed: " + (r == null ? "no result" : r.CombinedOutput),
                          ok ? MessageType.Info : MessageType.Error);
                if (refreshAfter) Refresh();
            });
        }

        void SetBanner(string text, MessageType type)
        {
            _banner = text;
            _bannerType = type;
        }

        // --- data ------------------------------------------------------------------

        sealed class Snapshot
        {
            public bool GitAvailable = true;
            public string Root;
            public GitStatus Status;
            public List<GitBranchInfo> Branches;
            public string Remote;
            public List<GitCommitInfo> Commits;
        }

        void Refresh()
        {
            var path = _repoPathInput;
            var includeHistory = _tab == (int)Tab.History;
            RunOp(async () =>
            {
                var s = new Snapshot();
                if (!GitRepository.IsGitAvailable())
                {
                    s.GitAvailable = false;
                    return s;
                }
                s.Root = await GitRepository.FindRootAsync(path);
                if (s.Root == null) return s;
                var repo = new GitRepository(s.Root);
                s.Status = await repo.GetStatusAsync();
                s.Branches = await repo.GetBranchesAsync();
                s.Remote = await repo.GetRemoteUrlAsync();
                if (includeHistory) s.Commits = await repo.GetLogAsync(HistoryLimit);
                return s;
            }, ApplySnapshot);
        }

        void ApplySnapshot(Snapshot s)
        {
            if (s == null) return;
            _gitAvailable = s.GitAvailable;
            if (!s.GitAvailable) return;

            _repoRoot = s.Root;
            _repo = s.Root == null ? null : new GitRepository(s.Root);
            if (s.Status != null) _status = s.Status;
            if (s.Branches != null) _branches = s.Branches;
            _remoteUrl = s.Remote;
            if (s.Commits != null)
            {
                _commits = s.Commits;
                _expandedCommits.Clear();
            }
        }

        // --- GUI -----------------------------------------------------------------

        void OnGUI()
        {
            DrawHeader();

            if (!_gitAvailable)
            {
                EditorGUILayout.HelpBox(
                    "git executable not found. Install git or set its full path in Settings.",
                    MessageType.Error);
                DrawSettings();
                return;
            }

            if (_repo == null)
            {
                DrawNoRepo();
                return;
            }

            DrawSyncBar();
            _tab = GUILayout.Toolbar(_tab, TabNames, EditorStyles.toolbarButton);
            EditorGUILayout.Space();

            switch ((Tab)_tab)
            {
                case Tab.Changes: DrawChanges(); break;
                case Tab.Branches: DrawBranches(); break;
                case Tab.History: DrawHistory(); break;
            }

            DrawSettings();
            DrawBanner();
        }

        void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Repo:", EditorStyles.miniLabel, GUILayout.Width(38));
            var newPath = EditorGUILayout.TextField(_repoPathInput);
            if (newPath != _repoPathInput) { _repoPathInput = newPath; }
            if (GUILayout.Button("Open", EditorStyles.toolbarButton, GUILayout.Width(50)))
                Refresh();
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(58)))
                Refresh();
            GUILayout.FlexibleSpace();
            if (_busy) GUILayout.Label("working…", EditorStyles.miniLabel, GUILayout.Width(58));
            EditorGUILayout.EndHorizontal();
        }

        void DrawNoRepo()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.HelpBox("This folder is not a git repository.", MessageType.Warning);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("git init", GUILayout.Width(120), GUILayout.Height(24)))
                RunGit("git init", () => new GitRepository(_repoPathInput).InitAsync());
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            DrawSettings();
            DrawBanner();
        }

        void DrawSyncBar()
        {
            EditorGUILayout.BeginHorizontal();
            var s = _status;
            var branchLabel = s == null ? "…" : (s.IsDetached ? "detached" : s.Branch);
            GUILayout.Label("⎇ " + branchLabel, EditorStyles.boldLabel, GUILayout.Width(140));
            if (s != null && !string.IsNullOrEmpty(s.Upstream))
            {
                var sync = s.Ahead == 0 && s.Behind == 0
                    ? "in sync"
                    : $"↑{s.Ahead} ↓{s.Behind}";
                GUILayout.Label(s.Upstream + "  " + sync, EditorStyles.miniLabel);
            }
            else if (s != null)
            {
                GUILayout.Label("no upstream", EditorStyles.miniLabel);
            }
            GUILayout.FlexibleSpace();
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && !_busy;
            if (GUILayout.Button("Fetch", GUILayout.Width(48)))
                RunGit("git fetch", () => _repo.FetchAsync());
            if (GUILayout.Button("Pull", GUILayout.Width(42)))
                RunGit("git pull", () => _repo.PullAsync());
            if (GUILayout.Button("Push", GUILayout.Width(42)))
                RunGit("git push", () => _repo.PushAsync());
            GUI.enabled = wasEnabled;
            EditorGUILayout.EndHorizontal();
        }

        // --- Changes tab -----------------------------------------------------------

        void DrawChanges()
        {
            if (_status == null) { EditorGUILayout.HelpBox("Loading status…", MessageType.None); return; }

            _changesScroll = EditorGUILayout.BeginScrollView(_changesScroll);

            var staged = new List<GitFileChange>();
            var unstaged = new List<GitFileChange>();
            var untracked = new List<GitFileChange>();
            var unmerged = new List<GitFileChange>();
            foreach (var c in _status.Changes)
            {
                if (c.IsUnmerged) unmerged.Add(c);
                else if (c.IsUntracked) untracked.Add(c);
                else
                {
                    if (c.HasStagedChange) staged.Add(c);
                    if (c.HasUnstagedChange) unstaged.Add(c);
                }
            }

            if (_status.Changes.Count == 0)
                EditorGUILayout.HelpBox("Working tree clean.", MessageType.Info);

            if (unmerged.Count > 0)
                DrawFileSection("Conflicts — resolve before committing", unmerged, null);

            DrawFileSection($"Staged changes ({staged.Count})", staged,
                c => RunGit("unstage", () => _repo.UnstageAsync(c.Path)));
            DrawFileSection($"Changes ({unstaged.Count})", unstaged,
                c => RunGit("stage " + c.Path, () => _repo.StageAsync(c.Path)));
            DrawFileSection($"Untracked files ({untracked.Count})", untracked,
                c => RunGit("stage " + c.Path, () => _repo.StageAsync(c.Path)));

            if (_status.Changes.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Stage All", GUILayout.Width(80)))
                    RunGit("stage all", () => _repo.StageAllAsync());
                if (GUILayout.Button("Unstage All", GUILayout.Width(90)))
                    RunGit("unstage all", () => _repo.UnstageAllAsync());
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            if (_showDiff && _selectedPath != null)
            {
                EditorGUILayout.Space();
                GUILayout.Label("Diff — " + _selectedPath, EditorStyles.boldLabel);
                _diffScroll = EditorGUILayout.BeginScrollView(_diffScroll, GUILayout.MaxHeight(180));
                EditorGUILayout.SelectableLabel(
                    string.IsNullOrEmpty(_diffText) ? "(no diff — file may be untracked)" : _diffText,
                    EditorStyles.textArea, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndScrollView();

            // Commit area
            EditorGUILayout.Space();
            GUILayout.Label("Commit message", EditorStyles.boldLabel);
            _commitMessage = EditorGUILayout.TextArea(_commitMessage, GUILayout.MinHeight(44));
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && !_busy && staged.Count > 0 && !string.IsNullOrWhiteSpace(_commitMessage);
            if (GUILayout.Button($"Commit {staged.Count} file(s)", GUILayout.Width(160), GUILayout.Height(22)))
                Commit();
            GUI.enabled = wasEnabled;
            EditorGUILayout.EndHorizontal();
        }

        void DrawFileSection(string title, List<GitFileChange> files, Action<GitFileChange> onToggle)
        {
            if (files.Count == 0) return;
            GUILayout.Label(title, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            foreach (var c in files)
            {
                EditorGUILayout.BeginHorizontal();
                var selected = _showDiff && _selectedPath == c.Path;
                if (onToggle != null)
                {
                    var staged = c.HasStagedChange && !c.IsUntracked;
                    var newVal = GUILayout.Toggle(staged, GUIContent.none, GUILayout.Width(18));
                    if (newVal != staged) onToggle(c);
                }
                else
                {
                    GUILayout.Space(18);
                }

                var label = c.OriginalPath != null ? c.OriginalPath + " → " + c.Path : c.Path;
                var style = selected ? EditorStyles.whiteLabel : EditorStyles.label;
                if (GUILayout.Button(new GUIContent(label, c.Path), style, GUILayout.ExpandWidth(true)))
                    SelectForDiff(c);
                GUILayout.Label(c.StatusLabel, EditorStyles.miniLabel, GUILayout.Width(70));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
        }

        void SelectForDiff(GitFileChange c)
        {
            _selectedPath = c.Path;
            _showDiff = true;
            _diffText = null;
            var repo = _repo;
            RunOp(() => repo.GetFileDiffAsync(c.Path), r =>
            {
                if (_selectedPath == c.Path)
                    _diffText = r == null ? "" : r.StdOut;
            });
        }

        void Commit()
        {
            var message = _commitMessage;
            RunGit("git commit", () => _repo.CommitAsync(message));
            _commitMessage = "";
        }

        // --- Branches tab ----------------------------------------------------------

        void DrawBranches()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("New branch:", GUILayout.Width(74));
            _newBranchName = EditorGUILayout.TextField(_newBranchName);
            _createAndSwitch = GUILayout.Toggle(_createAndSwitch, "switch after create", GUILayout.Width(130));
            var canCreate = !_busy && !string.IsNullOrWhiteSpace(_newBranchName);
            var wasEnabled = GUI.enabled;
            GUI.enabled = canCreate;
            if (GUILayout.Button("Create", GUILayout.Width(60)))
            {
                var name = _newBranchName.Trim();
                RunGit("git branch " + name, () => _repo.CreateBranchAsync(name, _createAndSwitch));
                _newBranchName = "";
            }
            GUI.enabled = wasEnabled;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            if (!string.IsNullOrEmpty(_remoteUrl))
            {
                EditorGUILayout.LabelField("origin", _remoteUrl, EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("origin", EditorStyles.miniLabel, GUILayout.Width(40));
                _newRemoteUrl = EditorGUILayout.TextField(_newRemoteUrl);
                wasEnabled = GUI.enabled;
                GUI.enabled = !_busy && !string.IsNullOrWhiteSpace(_newRemoteUrl);
                if (GUILayout.Button("Set remote", GUILayout.Width(80)))
                    RunGit("add remote", () => _repo.SetRemoteAsync(_newRemoteUrl.Trim()));
                GUI.enabled = wasEnabled;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();
            _branchScroll = EditorGUILayout.BeginScrollView(_branchScroll);
            if (_branches.Count == 0)
                EditorGUILayout.HelpBox("No branches yet — commit first to create the initial branch.", MessageType.Info);
            foreach (var b in _branches)
            {
                EditorGUILayout.BeginHorizontal();
                var nameStyle = b.IsCurrent ? EditorStyles.boldLabel : EditorStyles.label;
                GUILayout.Label(b.IsCurrent ? "●" : " ", GUILayout.Width(14));
                GUILayout.Label(b.Name, nameStyle, GUILayout.Width(140));
                var sync = b.Ahead == 0 && b.Behind == 0 ? "" : $" ↑{b.Ahead} ↓{b.Behind}";
                GUILayout.Label((b.Upstream ?? "") + sync, EditorStyles.miniLabel, GUILayout.Width(160));
                GUILayout.Label(b.Subject, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                wasEnabled = GUI.enabled;
                GUI.enabled = wasEnabled && !_busy && !b.IsCurrent;
                if (GUILayout.Button("Switch", GUILayout.Width(54)))
                    RunGit("checkout " + b.Name, () => _repo.CheckoutBranchAsync(b.Name));
                if (GUILayout.Button("Delete", GUILayout.Width(54)))
                {
                    if (EditorUtility.DisplayDialog("Delete branch",
                        "Delete local branch '" + b.Name + "'?", "Delete", "Cancel"))
                        RunGit("delete " + b.Name, () => _repo.DeleteBranchAsync(b.Name, false));
                }
                GUI.enabled = wasEnabled;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        // --- History tab -----------------------------------------------------------

        void DrawHistory()
        {
            _historyScroll = EditorGUILayout.BeginScrollView(_historyScroll);
            if (_commits.Count == 0)
                EditorGUILayout.HelpBox("No commits yet.", MessageType.Info);
            foreach (var c in _commits)
            {
                var expanded = _expandedCommits.Contains(c.Hash);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                var newExpanded = EditorGUILayout.Foldout(expanded,
                    c.AbbrevHash + "  " + c.Subject, true);
                if (newExpanded != expanded)
                {
                    if (newExpanded) ExpandCommit(c);
                    else _expandedCommits.Remove(c.Hash);
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label(c.Author + " • " + c.Date, EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();

                if (newExpanded)
                {
                    EditorGUI.indentLevel++;
                    if (c.Files == null)
                    {
                        GUILayout.Label("loading…", EditorStyles.miniLabel);
                    }
                    else
                    {
                        foreach (var f in c.Files)
                            GUILayout.Label(f, EditorStyles.miniLabel);
                    }
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        void ExpandCommit(GitCommitInfo c)
        {
            _expandedCommits.Add(c.Hash);
            var repo = _repo;
            RunOp(() => repo.GetCommitFilesAsync(c.Hash), files =>
            {
                c.Files = files ?? new List<string>();
                Repaint();
            });
        }

        // --- settings & banner ---------------------------------------------------------

        void DrawSettings()
        {
            EditorGUILayout.Space();
            _showSettings = EditorGUILayout.Foldout(_showSettings, "Settings", true);
            if (!_showSettings) return;
            EditorGUI.indentLevel++;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("git executable", GUILayout.Width(110));
            _gitPathInput = EditorGUILayout.TextField(_gitPathInput);
            if (GUILayout.Button("Apply", GUILayout.Width(54)))
            {
                GitRunner.GitPath = _gitPathInput;
                Refresh();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("user.name", GUILayout.Width(110));
            _identityName = EditorGUILayout.TextField(_identityName);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("user.email", GUILayout.Width(110));
            _identityEmail = EditorGUILayout.TextField(_identityEmail);
            EditorGUILayout.EndHorizontal();
            if (_repo != null)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                var wasEnabled = GUI.enabled;
                GUI.enabled = wasEnabled && !_busy
                    && !string.IsNullOrWhiteSpace(_identityName)
                    && !string.IsNullOrWhiteSpace(_identityEmail);
                if (GUILayout.Button("Save local identity", GUILayout.Width(140)))
                    RunGit("git config", () => _repo.SetIdentityAsync(_identityName.Trim(), _identityEmail.Trim()));
                GUI.enabled = wasEnabled;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
        }

        void DrawBanner()
        {
            if (string.IsNullOrEmpty(_banner)) return;
            EditorGUILayout.Space();
            _outputScroll = EditorGUILayout.BeginScrollView(_outputScroll, GUILayout.MaxHeight(90));
            EditorGUILayout.HelpBox(_banner, _bannerType);
            EditorGUILayout.EndScrollView();
        }
    }
}
