# Contributing

Bug reports, documentation improvements, and pull requests are welcome. This guide defines the expected workflow so the history and release branch stay easy to review.

## Branch and pull request workflow

`main` is the default and release branch. Keep it buildable and ready to release. Do not do feature work or routine fixes directly on `main`; make a branch and merge it back through a pull request. In GitHub repository settings, protect `main` by requiring pull requests and the successful `Build and release / build` check before merging. Require the branch to be up to date if practical, and disable force pushes and branch deletion for `main`.

For each change:

1. Start from an up-to-date `main` branch: `git switch main` then `git pull`.
2. Create one branch for one issue or coherent change. Use a lowercase prefix and short kebab-case description:
   - `feat/add-language-picker`
   - `fix/translation-cache-growth`
   - `docs/contributor-workflow`
   - `refactor/shared-chat-rules`
   - `chore/update-dependencies`
3. Make small, focused commits using the format below. Do not mix unrelated cleanup into a feature or fix.
4. Push the branch and open a pull request with `main` as the base. Open a draft PR early when feedback would help; mark it ready when the changes and checks are complete.
5. Set the PR title in the same Conventional Commits format as the commit subject; CI rejects titles that do not match. Review the diff, respond to feedback, and keep the PR checks green. Merge with **Squash and merge** so the PR title becomes one clean commit on `main`. Delete the feature branch after merging.

Use an issue first for bugs, features, and changes that need discussion or span multiple steps. Small typo fixes and clearly scoped maintenance can go straight to a PR. When a PR addresses an issue, link it with `Closes #123` in the PR description. Keep each PR focused enough to review as one unit; split independently useful changes into separate PRs.

Do not commit generated output, local settings, credentials, chat contents, or private user data. Before opening a PR, check `git status` and `git diff` so only intended source and documentation changes are included.

## Commit message format

Use the Conventional Commits format:

```text
<type>(optional-scope): <short imperative summary>
```

Use a lowercase type, scope, and summary, no period at the end, and keep the first line at 72 characters or fewer. Write the summary as an action, for example `fix: prune translations for expired messages`. CI enforces this format and length for PR titles.

Allowed types:

| Type | Use it for |
| --- | --- |
| `feat` | User-visible functionality. |
| `fix` | A bug fix. |
| `docs` | Documentation only. |
| `refactor` | Code restructuring without a behavior change. |
| `perf` | A performance improvement. |
| `test` | Adding or changing tests without changing production behavior. |
| `build` | Build scripts, packaging, or dependency tooling. |
| `ci` | Continuous integration workflow changes. |
| `chore` | Other maintenance that does not fit a more specific type. |
| `style` | Formatting-only changes with no behavior change. |
| `revert` | Reverting an earlier commit. |

Add a scope when it makes the change clearer, such as `fix(translator): retry failed setup`. Use `!` after the type or scope for a breaking change, and explain it in the body with a `BREAKING CHANGE:` footer. Add a body when context is needed; wrap long text and explain why the change is needed. Reference issues in the body when relevant.

Examples:

```text
feat(settings): remember the incoming language
fix: prune translations for expired messages
docs: explain the pull request workflow
refactor(bridge): share message validation rules
build!: require dotnet 10 sdk
```

Avoid vague messages such as `updates`, `fix stuff`, or `misc`. Each commit should make sense on its own. Before a PR is merged, squash work-in-progress commits and use a final message that follows this format.

## Issues and pull requests

Before opening an issue, check for an existing report. Bug reports should include the osu-chatter version, Windows version, osu! stable version when relevant, steps to reproduce, and expected versus actual behavior. Feature requests should explain the problem first, then describe the proposed behavior and alternatives considered.

PR descriptions should state the user-visible result, explain any important implementation decision, link the issue when one exists, and list the checks actually run. Keep screenshots and logs free of chat contents, private messages, API keys, usernames, and other personal data.

## Building and checking changes

Requirements: 64-bit Windows, the .NET 10 SDK or newer, and .NET Framework 4.8. Install the .NET Framework 4.8 Developer Pack if the build reports MSB3644.

Double-click `check.bat` to build and run the offline checks and application smoke check. Use `run.bat` to build and launch, or `package.bat` to build, check, and prepare a release ZIP. The launchers use `scripts/manage.ps1`, which is also used by CI. For terminal/CI usage, pass `-NoPause`:

```powershell
.\check.bat -NoPause
.\package.bat -NoPause -Version v1.0.0
```

Equivalent direct commands:

```powershell
dotnet build chatter.slnx -c Release
src\Chatter.Tests\bin\Release\net48\Chatter.Tests.exe
src\Chatter.App\bin\Release\net48\chatter.exe --smoke
```

The default checks use fake game data and a local HTTP fixture. They do not need a real DeepL key and do not send chat messages. Do not run `--live` or `--translation-live` as part of routine contributions: those checks attach to a running osu! client or download translation models.

Keep pull requests focused. Explain user-visible changes and mention which checks you ran.
