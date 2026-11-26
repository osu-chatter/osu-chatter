# Development checks

See [Contributing](../CONTRIBUTING.md#build-and-check) for prerequisites and the usual build/check commands. The launchers use [manage.ps1](../scripts/manage.ps1), which is also used by CI.

## Direct commands

From the repository root:

```powershell
dotnet build chatter.slnx -c Release
.\scripts\test-stable-monitor.ps1
.\src\Chatter.Tests\bin\Release\net48\Chatter.Tests.exe
.\src\Chatter.App\bin\Release\net48\chatter.exe --smoke
```

These build the app, test the compatibility monitor offline, run the application fixtures and check startup. The fixtures use fake game data and a local HTTP server; they don't need a live game, a DeepL key or a real chat send.

## UI checks

```powershell
.\check.bat -NoPause -ModernTranscriptCheck
```

With WebView2 installed, this also exercises the real modern chat view and transitions between chat and settings.

For repaint changes, use an unlocked Windows desktop:

```powershell
.\src\Chatter.Tests\bin\Release\net48\Chatter.Tests.exe --settings-paint-test artifacts\settings-paint
```

This briefly shows a fixture window. It compares retained screen pixels after scrolling and section changes against a complete repaint, saving images on failure. `DrawToBitmap` forces painting and can hide stale-pixel defects. The fixture uses synthetic model listings and doesn't change saved preferences.

## Translation and model checks

The test executable has opt-in modes for real translation:

| Mode | Purpose |
| --- | --- |
| `--translation-live` | Exercise the lightweight local translation runtime |
| `--install-model` | Install and exercise the Fast local AI preset |
| `--install-quality-model` | Install and exercise the Quality local AI preset |
| `--test-madlad` | Install and exercise MADLAD with CPU and Vulkan engines |
| `--test-plamo` | Install and exercise PLaMo with CPU and Vulkan engines |

These modes can download large runtimes and models. Inspect their implementation in [Chatter.Tests](../src/Chatter.Tests) before running them. The MADLAD/PLaMo modes use `artifacts/models`, require a Vulkan-capable GPU and about 2.5 GB of free disk space. They translate fixed Russian, Polish, English and Korean samples, including a protected URL. Timings include model startup/reloads where needed; GPU cases also check offloaded-layer diagnostics.

## Live osu! probes

Run these only when testing the integration against an idle osu!stable client:

| Mode | Effect |
| --- | --- |
| `--live` | Read the running game |
| `--native-live` | Temporarily replace local rendered chat lines, then request restoration |
| `--commands-live` | Install and remove the native command hook without submitting a message |
| `--inspect-commands-live <report>` | Record hook metadata, assembly identities and state without chat text |

Offline fixtures and no-send probes don't verify actual delivery. Test sending separately with a deliberately chosen target and message. The [compatibility guide](stable-compatibility.md) describes the layout assumptions.

`CHATTER_TRANSLATOR_ROOT` and `CHATTER_WEBVIEW_ROOT` isolate runtime storage for tests or portable launches. Generated reports, screenshots and models belong in ignored `artifacts/`.

## Manual game checks

Use an idle osu!stable client and check:

- Existing/new messages, text selection, scrolling, links, emoji, RTL text and window scaling.
- Translation, settings save/back behavior and draft preservation.
- Overlay positioning, mouse pass-through, scrolling and replies.
- Sending and `/t` to a chosen test conversation, including failure, cancellation and account/tab changes.
- Native translation and restoration after disable, pause, disconnect, gameplay and app exit.

For packaging and publication, see [Releases](../CONTRIBUTING.md#releases).

## Compatibility monitor

```powershell
.\scripts\test-stable-monitor.ps1
.\scripts\check-stable.ps1
```

The first command uses offline fixtures, including GitHub API mocks for automatic update issues. The second checks live upstream version metadata and doesn't create an issue locally. The [daily update watch](stable-compatibility.md#stable-compatibility-workflow) opens one review issue per changed upstream version. It is separate from required build checks; matching version metadata doesn't test reading, sending or display integration.

Run `scripts/test-stable-issue.ps1` directly to check report generation, issue formatting, open/closed issue deduplication, pagination and API failures. These tests never contact GitHub. Use the reporter's [dry-run mode](stable-compatibility.md#automatic-review-issues) to inspect the generated Markdown without posting it.
