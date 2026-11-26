# Contributing

Bug reports, fixes and documentation improvements are welcome. For a bug, include the Chatter version, Windows version, osu!stable build when relevant, steps to reproduce and what you expected to happen. Check existing issues first and remove private chat, API keys and personal data from attachments.

Open an issue before starting a feature or a change that needs discussion. Small typo fixes and clearly scoped maintenance can go straight to a pull request.

## Build and check

Use 64-bit Windows with the .NET 10 SDK or newer and .NET Framework 4.8. Install the .NET Framework 4.8 Developer Pack if the build reports `MSB3644`.

From the repository folder:

```powershell
.\run.bat -NoPause
```

This builds Release and launches Chatter. The other launchers are:

| Command | Purpose |
| --- | --- |
| `build.bat -NoPause` | Build Release |
| `check.bat -NoPause` | Build, run offline checks and check app startup |
| `check.bat -NoPause -ModernTranscriptCheck` | Also check the real WebView2 chat view and settings transitions |
| `package.bat -NoPause` | Build, check and create a local ZIP in `dist/` |

Without `-NoPause`, the launchers wait before closing so they can be used by double-clicking. They all use [scripts/manage.ps1](scripts/manage.ps1).

Routine checks use fixtures and don't need osu! or paid translation credentials. For direct commands, repaint checks, model tests and live-game probes, see [Development checks](docs/development.md).

## Branch and pull request workflow

Start from an up-to-date `main` and make a branch for each issue or coherent change, such as `fix/translation-cache` or `docs/model-setup`. Open a PR against `main`; keep it draft while work or required checks are outstanding. Link the issue with `Closes #123` when applicable.

Use lowercase Conventional Commits for commit subjects and PR titles, with an optional scope and a summary of at most 72 characters including the prefix:

```text
feat(settings): remember the incoming language
fix: prune translations for expired messages
docs: explain local model setup
```

Allowed types are `feat`, `fix`, `docs`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`, `style` and `revert`. Write the summary as an action without a trailing period. For a breaking change, add `!` after the type or scope and explain it with a `BREAKING CHANGE:` footer. CI checks PR title format and length.

Use the [PR template](.github/PULL_REQUEST_TEMPLATE.md): describe the problem and resulting behavior in **Summary**, add relevant **Implementation notes**, and list the checks you actually ran under **Checks**. If a check doesn't apply, explain why. Keep the PR focused and review the diff before pushing; leave generated output, models, local settings, credentials and chat data out of Git.

Merge reviewed PRs with **Squash and merge**, using the PR title as the final commit subject, then delete the branch. Maintainers should protect `main` with required PRs and the `Build and release / build` check, disable force pushes and deletion, and require up-to-date branches where practical.

## Releases

Pushing a `v*` tag publishes a release through [Build and release](.github/workflows/release.yml). Tag the merged `main` commit when ready to publish. Tags containing a hyphen are prereleases; other version tags create stable releases.

Before tagging, update the version in [Directory.Build.props](Directory.Build.props) and replace [RELEASE-NOTES.md](RELEASE-NOTES.md) with notes for that release. Run `package.bat -NoPause -Version <tag> -ModernTranscriptCheck`, perform the [manual game checks](docs/development.md#manual-game-checks), and inspect the extracted ZIP. The workflow publishes that ZIP and the release notes in the repository where it runs.

Use a new version when changing distributed bridge code: its assembly identity includes the release version, so reusing a version can leave osu! running an older loaded bridge.

## Documentation changes

Keep the [README](README.md) focused on what Chatter does and how to start. Put detailed usage in the relevant [guide](docs/README.md), and keep the packaged [START-HERE.txt](scripts/START-HERE.txt) usable as an offline quick start.

Check settings labels against `SettingsView.cs` and `SettingsView.Providers.cs`, defaults against `ExperienceOptions.cs`, and actions against the MainForm/overlay/reply code. Update the [compatibility reference](docs/stable-compatibility.md) when changing the integration and the release instructions above when changing packaging.

Keep documentation about the current implementation and release. Change history belongs in commits and past GitHub releases. For documentation-only changes, verify descriptions against source and check links and anchors.
