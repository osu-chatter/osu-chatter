# Release readiness audit · 2026-11-22

Scope: the combined changes prepared for `1.0.0` on `feat/release-1-0-0`, including source, documentation, workflows and the generated Windows ZIP. The earlier RC5 checks below are retained as historical evidence; final-version build/package checks are repeated for release preparation. Final acceptance and CI on the uploaded revision remain pending.

## Final 1.0.0 preparation

The isolated `v1.0.0` package command passed with zero build warnings/errors, all 53 offline application checks, 13 offline monitor checks, real WebView2 integration and normal/modern packaged startup. The final version uses the `Chatter.Bridge.Runtime_1.0.0` bridge identity. Version metadata, ZIP contents, document links, workflow publication arguments and absence of developer checkout markers are checked again before the preparation commit. Historical live checks below were performed on RC5; they are not presented as new live tests of the final bridge identity.

## Live checks completed

With osu! running, the reader passed with three open conversations, an active connection and menu/lobby state. About diagnostics read build `20261118.1` directly from **live configuration memory**, with stream `stable40` and a matching executable fingerprint. The native display probe reported 11 replaced rendered lines, verified unchanged original message text/IDs/link sources, exercised display renewal/expiry and returned successfully after its restoration request.

The no-send `--commands-live` probe was subsequently repeated successfully: bridge startup and native-command hook installation passed, and the stop request returned without a cleanup error. No chat message was submitted. The diagnostic inspector's Harmony prefix lookup was corrected to read the public field instead of a property; the offline command-hook fixture covers that lookup.

## Checks performed

| Check | Result |
| --- | --- |
| Isolated Release build | Passed, zero warnings and errors. |
| Offline application suite | All 53 checks passed, including settings, persistence, translation protocols, context/cache, Unicode/links, native command fixture hooks, bridge upgrades and display protocols. |
| Offline compatibility monitor | All 13 checks passed. |
| Real WebView2 integration | Passed text/RTL/emoji, selection, keyed refresh and scroll preservation checks. |
| Startup from build and packaged EXE/config | Normal and modern-transcript startup passed with embedded dependencies. |
| Real lightweight translation | English/Russian/Polish/Korean cases passed, including auto-detection, restart after a killed worker and process cleanup. |
| Real Local AI translation | Existing Fast and Quality models passed Russian/Polish translation fixtures. Quality still mistranslates some informal text; this is documented, not an accuracy guarantee. |
| Settings presentation | All six sections plus narrow and DeepL variants rendered; screenshots inspected. |
| Update metadata | Live monitor reported Stable `20261118.1`, matching the recorded baseline; this is not a live integration test. |
| NuGet advisories | No vulnerable direct/transitive packages reported by the configured sources at audit time. This does not audit separately downloaded Python dependencies, engines or models. |
| Upload contents | Limited private-key/GitHub/OpenAI-token pattern scan found no matches. Generated outputs/models remain ignored. This is not a comprehensive secret audit. |
| Scripts/workflows | PowerShell and Python syntax passed; both workflow YAML files parsed. Stable/prerelease publication arguments passed with a mocked CLI; nothing was published. |
| Documentation | Local Markdown targets and code fences checked; `git diff --check` passed. Instructions were compared with the implemented settings, commands and packaging behavior. |
| Final archive | Expected eight files only, matching executable/config/version; ZIP CRC passed. Bundled documentation links resolve locally or point online. |

The audit used installed translation runtimes/models; it did not repeat multi-gigabyte clean downloads or validate every supported language pair. Cloud protocols were exercised with fixtures, without a paid account or a real cloud translation request.

## Packaging issue corrected

The repository Markdown contains relative links to guides/source absent from the release ZIP, and the README logo was also absent. Packaging now converts repository-only links and the logo to online repository URLs, while preserving local links such as LICENSE, fragments and external URLs. A fixture checked conversion/preservation and the final ZIP excluded an extra file deliberately left in staging. Repository Markdown remains unchanged by packaging. The stable package ships RELEASE-NOTES instead of historical candidate notes. See [releasing.md](releasing.md) for archive contents.

A subsequent portability check found the developer checkout path in compiler PDB references inside the EXE and its embedded project libraries. It was debug metadata, not a runtime dependency. The shared build now maps that checkout root to `/_/`; the rebuilt EXE was checked for the current user's name and checkout/drive markers in UTF-8 and UTF-16. Dependencies can retain their upstream build metadata. Runtime storage is based on the current user's folders, and the game directory is discovered from the process. Fixed public download/API/repository URLs and configurable loopback defaults remain intentional. A clean Windows installation and first-time downloads on another PC have not been validated by this audit.

## Remaining release gates

1. **Live game acceptance:** the checks above confirm reader and version-memory diagnostics, a successful display probe and the no-send command-hook probe. Overlay behavior over the actual game and actual translated sending remain unverified in this audit. Complete the deliberately chosen target/message checks in [release-candidate.md](release-candidate.md#owner-acceptance-before-publishing), including `/t`, failure/cancellation, game-tab/account changes and gameplay transitions. Existing historical live evidence is recorded there and in [stable-compatibility.md](stable-compatibility.md).
2. **CI on the uploaded revision:** the public repository was reachable and the latest existing main/feature build runs were successful. Those runs cover earlier remote commits, not this working tree. Add all intended new files, commit/push through the contributor workflow and require the new build to pass before merging/tagging. No upload, merge or tag was performed in this audit.
3. **Main branch protection:** GitHub's public branch API reported `protected: false` for main. Configure PR/build requirements in repository settings as described in [CONTRIBUTING.md](../CONTRIBUTING.md#branch-and-pull-request-workflow). Public inspection did not establish any other organization-level enforcement.
4. **Release identity and acceptance:** shared default version is `1.0.0`; the app inherits it, About/START-HERE have no candidate label, and the package uses reviewed RELEASE-NOTES. Complete review and acceptance before tagging the merged main commit, following [releasing.md](releasing.md). Preparing the version does not publish it.

## Reproduction

```powershell
./scripts/manage.ps1 -Task Package -NoPause -Version v1.0.0 -ArtifactsPath artifacts/release-1.0.0 -ModernTranscriptCheck
./scripts/check-stable.ps1
dotnet list chatter.slnx package --vulnerable --include-transitive
```

Additional fixture modes used: `--modern-transcript-test`, `--translation-live`, `--install-model`, `--install-quality-model`, and `--settings-shots`. The model modes use the test program's artifacts model directory; see their implementation before running them on another machine to avoid unexpected large downloads. Generated screenshots and audit fixtures are under ignored `artifacts/release-audit/`. Final archives are under ignored `dist/`.
