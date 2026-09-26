# unity-git-client

In-editor Git client for Unity: status, commit, branch, push/pull, and history from a custom Editor window. No native dependencies — it drives the `git` CLI asynchronously so the editor never blocks.

## Features

- **Changes**: staged / unstaged / untracked files with checkboxes, per-file diff view, Stage All / Unstage All
- **Commit**: message box + one-click commit of staged changes (identity configurable in Settings)
- **Branches**: list with current-branch marker and ahead/behind counts, create / switch / delete, set `origin` remote
- **Push / Pull / Fetch**: one-click buttons; push automatically sets upstream (`-u origin HEAD`)
- **History**: scrollable commit log (hash, subject, author, date) with expandable per-commit file lists
- **Robustness**: detects missing `git`, non-repo folders (one-click `git init`), detached HEAD, empty repos, merge conflicts; all output and errors shown in the window

## Requirements

- Unity 2021.3 LTS or newer
- `git` on the system `PATH` (or set a custom path in the window's Settings section)

## Install

### Unity Package Manager (git URL)

1. **Window → Package Manager → + → Add package from git URL…**
2. Paste:
   ```
   https://github.com/CommunityPokeOrg/unity-git-client.git?path=Packages/com.communitypoke.unitygit
   ```

### Embedded package

Copy `Packages/com.communitypoke.unitygit` into your project's `Packages/` folder. Unity picks it up automatically.

## Usage

Open **Window → Git Client** (also under **Tools → Git Client**). The window defaults to your Unity project root; point "Repo" at any other folder to manage a different repository.

| Tab | What it does |
|-----|--------------|
| Changes | Stage/unstage files, view diffs, commit staged changes |
| Branches | Create, switch, delete branches; manage the origin remote |
| History | Browse the commit log; expand a commit to see its files |

The toolbar shows the current branch, upstream ahead/behind state, and Fetch / Pull / Push buttons. A status banner at the bottom surfaces the output (or error) of the last git operation.

## Repository layout

```
Packages/com.communitypoke.unitygit/   # the package (install this)
  Editor/                              # EditorWindow + git layer (CommunityPoke.UnityGit.Editor)
  Tests/Editor/                        # NUnit parser tests (Unity Test Framework)
Assets/                                # empty — scaffold so the repo opens as a Unity project
Packages/manifest.json                 # project manifest
ProjectSettings/ProjectVersion.txt     # pinned editor version (2022.3 LTS)
```

## Design notes

- The git layer (`GitRunner`, `GitRepository`, `GitStatusParser`, `GitModels`) is pure `System.*` — no `UnityEngine`/`UnityEditor` references — so it compiles and runs in a plain .NET environment and is covered by the EditMode tests.
- `GitClientWindow` is IMGUI-based for maximum compatibility across editor versions.
- Process calls use `GIT_TERMINAL_PROMPT=0` so operations fail fast with a visible error instead of hanging on credential prompts.

## License

MIT — see [LICENSE](LICENSE).
