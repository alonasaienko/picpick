# Contributing

How we work with Git and GitHub in this project.

## TL;DR

1. Take an issue from the board and move it to **In progress**.
2. Create a branch from fresh `main`.
3. Commit, push and open a pull request with `Closes #<issue>`.
4. Get one approval, then click **Squash and merge**.

Never push to `main` directly.

## 1. Issues (tasks)

Every piece of work starts as a GitHub issue.

### Naming

Format: **`[task] What to do: details`**. The task prefix is the course item number (`п.3`, `п.4`…), or `setup` for repo chores.

- `[п.3] Requirements for zone B: FR, NFR, System actor`
- `[п.5] Error format conventions`
- `[п.6] Concurrency scenarios for zone C`
- `[setup] Repository structure and glossary`

### Size

An issue should take **1–3 days** and produce **one clear result**.

- Too big: `Do п.3`.
- Too small: `Write FR-201`.

### What to fill in

- **Description:** 1–3 sentences on what to do.
- **Definition of Done:** a checklist, for example:
  ```markdown
  - [ ] At least 4 FRs with ID, actor, priority and acceptance criteria
  - [ ] Mermaid diagram renders on GitHub
  ```
- **Assignee:** who does it.
- **Labels:**
  - course item: `п3-srs`, `п4-architecture`, `п5-errors`, `п6-concurrency`;
  - zone: `zone-a`, `zone-b`, `zone-c`, `shared`;
  - type: `docs`, `code`.
- **Milestone:** the deadline (`Deadline 1: 29.10`, `Deadline 2: 10.12`, `Demo: 17.12`).

## 2. Project board

Columns: **Todo → In progress → In review → Done**

| Column | Meaning |
|---|---|
| Todo | Not started |
| In progress | Someone is working on it, branch exists |
| In review | Pull request is open |
| Done | Merged into `main` (moves automatically via `Closes #N`) |

Keep your cards up to date. The board is how we see progress without calls.

## 3. Branches

**One issue = one branch = one pull request.**

Format: **`type/issue-number-short-description`**. Lowercase, latin letters, hyphens.

| Type | When | Example |
|---|---|---|
| `docs` | documentation, diagrams | `docs/12-srs-zone-b` |
| `feat` | new code | `feat/25-worker-watermark` |
| `fix` | bug fix or correction | `fix/31-openapi-validation` |
| `chore` | setup, configs, structure | `chore/1-repo-setup` |

Branches should live **1–3 days**. The longer a branch lives, the more conflicts it collects.

## 4. Commits

Format: **`type(scope): description`**

- `docs(srs): add FR-201..204 for zone B`
- `docs(concurrency): add competing consumers scenario`
- `feat(worker): add watermark with Pillow`
- `chore: add .gitignore`

Commit small and often, at least once per work session.

## 5. Pull requests

- **Title:** same as the issue, e.g. `[п.3] Requirements for zone B`.
- **Description:** must contain `Closes #<issue number>`.
- **Reviewer:** pick by rotation.

  | Author | Reviewer |
  |---|---|
  | Alona | Yuliia |
  | Uliana | Alona |
  | Yuliia | Uliana |

- **Review within 24 hours.** A review is more than "LGTM": leave at least 1–2 questions or comments on the content.
- **Merge:** use **Squash and merge** only. The branch is deleted automatically.

## 6. Step by step

```bash
# 1. Start from fresh main
git switch main
git pull

# 2. Create your branch
git switch -c docs/12-srs-zone-b

# 3. Work, then commit
git add docs/03-srs/zone-b-processing.md
git commit -m "docs(srs): add FR-201..204 for zone B"

# 4. Push and open a PR on GitHub
git push -u origin docs/12-srs-zone-b

# 5. Fix review comments in the same branch
git add .
git commit -m "docs(srs): clarify acceptance criteria"
git push
```

### If `main` changed while you were working

```bash
git switch main
git pull
git switch docs/12-srs-zone-b
git merge main
# resolve conflicts if any, then:
git push
```

Prefer buttons? Everything above also works in **GitHub Desktop** or the **Source Control** tab in VS Code.

## 7. Rules

1. **No direct pushes to `main`.** Only through pull requests.
2. **Edit only your own files.** Shared files (`docs/glossary.md`, `docs/06-concurrency/patterns.md`) are changed in a separate small PR and merged quickly.
3. **Names come from the [glossary](docs/glossary.md).** Need a new term? Add it there first.
4. **Use your number range:** zone A uses `1xx`, zone B `2xx`, zone C `3xx` (FR, NFR, endpoints, scenarios).
5. **No secrets in the repo.** Real values go to `.env` (ignored); commit only `.env.example`.
6. **No large files.** Keep test photos local; commit only a few small samples if needed.
7. **Diagrams as code.** Write Mermaid in ` ```mermaid ` blocks and check that they render in the PR preview.