# Historical combined release candidate · 1.0.0-rc.5

This document preserves the pre-release validation record for RC1–RC5. The release being prepared is now `1.0.0`; see [RELEASE-NOTES.md](../RELEASE-NOTES.md) and the [current readiness audit](release-readiness.md). The remaining checks below are historical acceptance criteria, not user installation instructions.

## Version detection and monitor repair in RC5

The monitor now uses the public `/home/changelog?stream=stable40` JSON route, which returns HTTP 200 here, instead of the API route returning 403. The tested executable is now identified as Stable `20261118.1`: its existing SHA-256, local LastVersion/stream, and MD5 match the official updater manifest and public changelog. The live monitor passes. A matching version remains update metadata, not functional compatibility certification.

The About compatibility report now includes the game build and source, read from live configuration memory with a configuration-file fallback using the Reborn approach. The extra scan runs asynchronously only on explicit inspection. New checks cover parsing, independent configuration-memory layout/mutation fixtures, conflicting configuration files and the public endpoint request. Actual installed configuration returned `b 20261118.1`; the 2026-11-22 live follow-up also confirmed the same build directly from running-client configuration memory. See the compatibility guide for every added signature/offset.

## Message menu fix in RC4

Selecting a language in “Translate to…” disposed its ContextMenuStrip inside Closed, before Windows Forms finished reparenting the dropdown window. This raised ObjectDisposedException after the selection. The built-in message menu used the same unsafe cleanup. Both menus now belong to the transcript, are reused between openings, and are disposed with that control. A regression check reproduced the exact reported stack before the fix and now selects all 14 languages through both transcript action paths, checks the message and target dispatched exactly once, cancels/reopens the picker, and verifies menu cleanup when the transcript closes. These checks use fixture messages and never send chat messages.

## Command loading and settings latency in RC3

The running client was inspected without sending chat messages. It contained only the legacy `Chatter.Bridge, Version=1.0.0.0`, which has no native-command handler, despite RC2 being active. CLR weak-name binding through ExecuteInDefaultAppDomain reused that assembly after a version-only upgrade. The bridge's simple assembly name now includes the release version, and the app/core/sidecar extraction share that identity through generated metadata. A regression fixture reproduces the version-only failure using the real CLR host stub and verifies that a changed identity runs the new code.

Command hooks now use individual Harmony owner IDs, so delayed cleanup from an older session cannot unpatch a replacement. A live no-send probe successfully installed and removed the current hook alongside the old assembly. `/t` and `/translate` are consumed before osu!'s internal slash-command dispatcher; other commands still pass through. Chatter shows `/t ready`, connecting/paused, or unavailable in its translation status. Actual delivery of a translated test message remains an owner acceptance check.

Settings formerly constructed every section and synchronously scanned model folders before becoming visible. Only the selected section is now constructed, model inventory runs on a background task on first Models visit or explicit refresh, and row measurements/layout are reused. Settings opening measured about 304 ms on this PC during the integration check. Tests verify unopened sections remain unbuilt, a blocked model inventory does not block navigation, and saving preserves untouched settings.

## Settings revision in RC2

Settings now replace the chat content inside the main window, with the conversation sidebar retained. The modal dialog and native tab strip are removed. Sections use the app's dark cards, rounded fields, switches and scrollbar. Automatic/manual providers are configured inline; credentials, URLs and local-model actions appear only for relevant engines. Browse buttons sit beside file paths. Save applies without leaving settings; Back/Escape returns to the same draft and discards unsaved edits. Background reading continues.

Regression checks cover embedded ownership, draft preservation, save/back semantics, narrow layouts and scrolling, conditional provider fields, credential isolation, and unchanged settings retaining translation results. All six sections were rendered for visual inspection, including narrow and cloud-provider variants.

## Included

- Standalone chat, movable/resizable interactive or click-through overlay, and native osu! chat translation.
- Native translation-only/bilingual/original display and `/t <language> <message>` / `/translate` outgoing commands.
- Stable transcript progress area, keyed DOM updates, history policy, visible-message priority, manual retry and per-message errors.
- Selectable color-emoji/RTL transcript, message actions, search, channel scrolling and keyboard shortcuts.
- Expanded settings, window persistence, topmost/tray/private-message notifications, source overrides and character counts.
- Local Argos without the LibreTranslate web-server stack; free local Hy-MT2 Fast/Quality models; external chat-completions adapter; existing DeepL/LibreTranslate; original-text chat mode.
- Bounded preceding context, terminology guidance, link/map-title protection, in-memory cache, model installation/pre-download/disk tools.
- x86/structural compatibility checks, ambiguity rejection, typed errors, fingerprint diagnostics and a separate daily upstream-version watch.

## Validation

The Release solution builds with zero nullable warnings. The offline suite covers translation protocols, persistence, transcript anchors, links, Unicode, native display leases/restoration and an isolated Harmony sender fixture (exact aliases suppressed, ordinary messages unchanged, failed draft restoration, unpatch).

The standalone packaged executable also checks modern transcript startup using only its EXE/config and embedded dependencies. A real WebView2 integration check verifies literal HTML text, emoji/surrogate link positions, RTL direction, keyed node reuse, selection and scroll preservation. A clean Argos runtime on G: translated English/Russian/Polish/Korean and recovered from a killed worker. Both local AI presets were installed from verified downloads and exercised with real Russian/Polish text. Larger models still make errors: the phonetic Russian “мейд ин чайна” became “maid in China,” not “made in China.” CPU timings on this PC were about 19 seconds for a cold quality-model request and 5 seconds warm with two threads; performance varies by hardware and text.

The native reader was exercised against the running x86 client. Native display replacement/restoration was validated earlier on the recorded executable. Native command capture and unpatch are verified in an isolated fixture; the RC3 live no-send probe successfully installed and removed the real hook. Actual translated sending remains an owner acceptance check. No real chat message was submitted during automated testing.

## Owner acceptance before publishing

1. Close the older Chatter instance, then run this folder's chatter.exe.
2. Check busy existing history, new incoming messages, selecting/copying text, scrolling, emoji/RTL, resizing and Windows scaling.
3. In Settings → Overlay, enable it. Drag/resize/scroll, lock/unlock with Ctrl+Shift+O, open reply with Ctrl+Shift+Enter, check languages and preview. Verify errors retain the draft.
4. Install/select a local model in Settings → Models and Translation. Check translation speed, context, source overrides and terms against real conversations.
5. Enable native incoming translation in menus/lobby. Check bilingual mode, game tab switches, long lines, links and restoration on disable/pause/gameplay/account changes.
6. Enable native commands and deliberately choose a test target/message before submitting `/t en ...`. Check success, failure, channel switches and cancellation. Sending is never automatically retried.
7. Confirm the candidate is good before requesting the official merge/tag/release.

## Compatibility limits

The integration is for osu!stable x86, not lazer. The external overlay requires windowed/borderless rendering. Native operations are limited to menus/lobby and depend on the private client layout. A single local fingerprint is recorded and identified as Stable `20261118.1`. Structural checks do not establish support for every release. The separate daily workflow flags upstream version differences for manual review. Its public JSON feed and recorded baseline now pass the live comparison; future version differences still require manual review. See [stable-compatibility.md](stable-compatibility.md#stable-compatibility-workflow) for its limits and separate reader/sender/display checks.

Models are downloaded separately and are not included in the ZIP. The fast model trades accuracy for latency; the quality model needs substantially more RAM and CPU time. No model guarantees correct informal chat.

## Repository audit (2026-11-22)

The compatibility guide now describes the implemented scanner, architecture/member checks, pipe operations and startup errors, rather than their earlier proposed state. The publishing job explicitly identifies the repository despite having no checkout; prerelease tags are marked as GitHub prereleases and do not replace the latest stable release. Contributor branch/PR policy is unchanged; verification and release instructions were expanded. The composer also has an undoable Ctrl+Backspace word-deletion regression check. See [Preparing a release](releasing.md) before tagging.

The current working tree passed a Release build with zero warnings/errors, 53 offline app checks, 13 offline monitor checks, real WebView2 integration and startup smoke checks. Both workflow files parse, local documentation links resolve, and the publishing step's stable/prerelease arguments were checked with a mocked CLI without publishing. No GitHub-hosted workflow run or new live game validation was performed in this audit. RC5 includes the updated diagnostics and documentation; older archives remain separate.

The daily upstream-version workflow, scripts, JSON baseline and offline tests are retained as an early warning. It distinguishes changed versions from unavailable metadata and an unverified baseline. Runtime integration safeguards remain independent of it. The guide now distinguishes changing process addresses from stable offsets and documents every native renderer/input member. Read-only source comparison confirmed unchanged reader signatures/offsets and CLR layout; the inspected executable contains all 14 refreshed sender-related names and none of the earlier equivalents.

## Reproducing checks

The user documentation was also reconciled with the current settings, translation, overlay/reply and packaging code on 2026-11-22. README and the packaged quick start now cover the actual settings labels, keyboard shortcuts, advanced model controls, storage, tray behavior and native-command limits. Contributor/release guides describe documentation upkeep and which files ship in the ZIP. This documentation pass checked local Markdown link targets and whitespace; it did not rerun the build or live game probes, and does not add functional acceptance evidence to the checks recorded above.

```powershell
./scripts/manage.ps1 -Task Package -NoPause -Version 1.0.0-rc.5 -ArtifactsPath artifacts/rc-package -ModernTranscriptCheck
```

Optional live probes: Chatter.Tests.exe --live, --native-live, --commands-live, --translation-live, --install-model, --install-quality-model. Native display probes alter local renders temporarily; command probes install/remove a hook without sending. Use an idle test game. CHATTER_TRANSLATOR_ROOT and CHATTER_WEBVIEW_ROOT isolate runtime storage for tests or portable launches.
