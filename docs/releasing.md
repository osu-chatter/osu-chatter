# Preparing a release

Version `1.0.0` is prepared on `feat/release-1-0-0` as one owner-requested combined pull request targeting `main`. The PR/commit title is `feat: add chat translation upgrades and prepare release 1.0.0`. Local checks do not approve publication: finish review, CI and the remaining manual checks before tagging. [RELEASE-NOTES.md](../RELEASE-NOTES.md) is the reviewed release description; [release-candidate.md](release-candidate.md) preserves the historical RC validation record.

The [2026-11-22 readiness audit](release-readiness.md) records the current local checks, packaging correction and remaining live/CI/repository-setting requirements.

## Before tagging

1. Review the complete diff, including newly added files. Keep generated output, local models, settings, credentials and chat data out of Git. Routine contributor branch/PR rules remain in [CONTRIBUTING.md](../CONTRIBUTING.md); the owner authorized this combined candidate as a one-time exception to feature separation.
2. Complete the candidate's manual acceptance checks on the intended osu! client. Record the executable fingerprint and paths actually exercised in the [compatibility guide](stable-compatibility.md). Only record an official build identifier after establishing that it belongs to that tested executable.
3. Keep the release version in `Directory.Build.props` consistent with `RELEASE-NOTES.md` and packaged `scripts/START-HERE.txt`. The app inherits the shared version; a tag build passes its version as an MSBuild override. Check README feature usage and quick-start settings labels against the app. Each shipped bridge uses its release version in its assembly identity; do not replace an already distributed release with changed bridge code under the same version.
4. Run the release checks and inspect the archive:

   ```powershell
   ./scripts/manage.ps1 -Task Package -NoPause -Version <approved-version> -ModernTranscriptCheck
   ```

   Replace `<approved-version>` with the chosen tag, `v1.0.0` for this release. The command builds Release, runs offline app and compatibility-monitor checks, exercises WebView2, checks normal and modern packaged startup, and creates `dist/osu-chatter-windows-x64.zip`. It does not publish anything. Models/runtimes download separately; embedded dependency notices ship in the ZIP.
5. Review the resulting app and archive, then obtain the owner's final release acceptance before pushing a release tag.

The ZIP contains `chatter.exe`, `chatter.exe.config`, `README.md`, `START-HERE.txt`, `VERSION.txt`, `LICENSE`, `THIRD-PARTY-NOTICES.txt` and `RELEASE-NOTES.md` at its root. Packaging keeps links to bundled files local and converts links to repository-only guides/source into GitHub links; the README logo also loads from the repository online. The full guides/source and logo therefore require internet access, while the packaged usage text remains readable offline. Check that the packaged instructions and version describe this archive, and that no extra local files entered it. For an `-rc.` or `-rc-` version, the script also keeps a versioned candidate ZIP alongside the fixed archive filename.

## What GitHub publishes

[Build and release](../.github/workflows/release.yml) runs build/checks on branch pushes and pull requests. Pushing a tag starting with `v` also packages the app and runs the dependent publishing job. A manually dispatched run against such a tag follows the same release path. Treat pushing a release tag as publication authorization.

The publish job uses the current repository explicitly, requires the tag to exist, and creates a GitHub release with the reviewed `RELEASE-NOTES.md` and the ZIP. Both the ZIP and notes are passed from the build job in its artifact. Tags with a prerelease suffix are marked as prereleases and do not become the latest stable release. See the [GitHub CLI release options](https://cli.github.com/manual/gh_release_create). A stable tag creates a normal release. The workflow's ordinary build path uses offline tests and startup checks; run the additional real WebView2 integration locally before tagging.

For this combined PR, open the scope issue first, reference it with `Closes #<number>`, and retain the normal Conventional Commits, review and squash-merge requirements. Keep it draft while required checks or manual acceptance remain outstanding. After approval, squash-merge into main, fetch the merged commit and create an annotated `v1.0.0` tag on that commit. Never tag the preparation branch or push a release tag merely to test the pipeline.


The separate [stable compatibility watch](stable-compatibility.md#stable-compatibility-workflow) is an early upstream-update signal. It does not replace manual game checks or owner acceptance, and it never publishes the app. A matching upstream version alone does not verify the reader, sender, renderer or command hooks.
