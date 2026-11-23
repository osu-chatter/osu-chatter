# osu-chatter

<p align="center">
  <img src="assets/chatter-logo.png" alt="osu-chatter logo" width="280">
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Made%20in-C%23-68217A?style=for-the-badge&logo=csharp&logoColor=white" alt="Made in C#">
  <img src="https://img.shields.io/badge/platform-Windows-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="Platform: Windows">
  <a href="https://osu.ppy.sh/home"><img src="https://img.shields.io/badge/osu!-E3619B?style=for-the-badge&logo=osu&logoColor=white" alt="osu!"></a>
</p>

<p align="center">
  <a href="https://github.com/osu-chatter/osu-chatter/releases/latest"><img src="https://img.shields.io/github/v/release/osu-chatter/osu-chatter?label=latest%20release&style=for-the-badge" alt="Latest release version"></a>
  <a href="https://github.com/osu-chatter/osu-chatter/releases"><img src="https://img.shields.io/github/downloads/osu-chatter/osu-chatter/total?label=downloads&style=for-the-badge&logo=github" alt="GitHub release downloads"></a>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-PolyForm%20Noncommercial-555555?style=for-the-badge" alt="License: PolyForm Noncommercial"></a>
</p>

<p align="center">
  <a href="https://ko-fi.com/m4lform"><img src="https://img.shields.io/badge/Ko--fi-support%20the%20project-FF5E5B?style=for-the-badge&logo=ko-fi&logoColor=white" alt="Support osu-chatter on Ko-fi"></a>
</p>

**A chat companion for osu!stable.** Read chats you already have open in the game, translate messages, and reply from Chatter or its optional overlay. Incoming translations can also appear directly in the game's own chat.

This is an unofficial community project. It is not affiliated with osu! or its developers.

## What it does

- Shows open channel and private-message tabs. Join new channels in osu!stable.
- Shows a profile picture beside each person, including small cached animations (oversized animations use a still thumbnail in the modern transcript), and a quiet gold highlight for an active supporter.
- Opens web and osu! links from chat, including the map or song on an "is playing" or "is listening" line.
- Keeps your reading position as messages arrive.
- Translates incoming and outgoing messages using lightweight local translation, local AI, a chat-completions endpoint, DeepL, or LibreTranslate. Automatic translation and the Translate button can use different providers.
- Sends only when you press **Send** or **Enter**, including submitting an enabled `/t` command in osu!. Messages are not retried automatically.

## Download

Windows builds are published on the [Releases page](https://github.com/osu-chatter/osu-chatter/releases). Extract the full ZIP and run `chatter.exe`. Keep `chatter.exe.config` beside it.

Chatter runs on 64-bit Windows 10 or 11 with .NET Framework 4.8. You don't need Visual Studio, the .NET SDK, or a separate Python install to run a release.

The integration targets the 32-bit osu!stable client; osu!lazer is not supported. Reading and sending depend on the client's internal layout. Sending, native incoming translation and native commands require menus or the lobby. The external overlay requires windowed/borderless mode.

## First conversation

1. Open osu!stable, sign in, and open the channel or private conversation in the game.
2. Start Chatter and choose a translation provider and default languages. For Local AI, install an engine/model in **Settings → Models**, select **Local AI** in **Settings → Translation**, and save.
3. Select the conversation in Chatter. Set the incoming language and automatic translation as needed; the source/target controls below the composer set your outgoing languages.
4. Type a draft. **Preview** shows its translation without sending. **Send** or **Enter** translates and submits it once. The translated result must fit the 400-character limit; shorten the draft if the preview exceeds it.

**Pause** stops reading and automatic translation, hides the overlay and stops native integration; **Resume** starts reading again. To join another channel or open a new private conversation, use osu!stable.

## Build and run

To build from source, install the .NET 10 SDK and .NET Framework 4.8. If the build reports `MSB3644`, install the .NET Framework 4.8 Developer Pack too. From the repository folder, run `run.bat` to build and launch chatter.

The other launchers are `build.bat` to build, `check.bat` to run the offline checks, and `package.bat` to create a release ZIP in `dist/`. Add `-NoPause` when running one from PowerShell or automation. For example:

```powershell
.\check.bat -NoPause
.\package.bat -NoPause -Version v1.0.0
```

For contribution and review conventions, see [CONTRIBUTING.md](CONTRIBUTING.md). Version 1.0.0 changes are described in [RELEASE-NOTES.md](RELEASE-NOTES.md); validation is recorded in the [readiness audit](docs/release-readiness.md).

Game offsets, signatures, and reflected member names are maintained together in [`src/Shared/StableCompatibilityProfile.cs`](src/Shared/StableCompatibilityProfile.cs). See the [compatibility guide](docs/stable-compatibility.md) for the address-discovery method, complete layout/member inventory, observed candidate changes and maintenance instructions. Settings → About can inspect the game build from live configuration or the last-launched version in its configuration files, along with the executable fingerprint. A separate [daily compatibility watch](docs/stable-compatibility.md#stable-compatibility-workflow) warns when upstream version metadata changes; the game integration still needs independent reading/sending/display checks.

## Translation

On first launch, choose a provider and languages. You can change them later in **Settings → Translation**. Settings open inside the main window. Choose a section on the left, save changes to apply them, and use **Back to chat** or **Esc** to return to your conversation. Unsaved changes are discarded when leaving. Sections remember their scroll positions while Settings is open.

- **Local AI (free on your PC):** Settings → Models installs a checksum-verified llama.cpp CPU or Vulkan engine and translation model. Fast uses Hy-MT2 1.8B Q4 (about 1.1 GB); Quality uses Hy-MT2 7B Q4 (about 4.6 GB); MADLAD-400 uses 3B Q4 (about 1.9 GB); PLaMo Translate uses 2B Q4 (about 1.8 GB). Choose a download drive with at least 1.5 / 6.5 / 2.5 / 2.5 GB free respectively. Select Local AI in Settings → Translation after installation. Hy-MT2 stays loaded between requests. MADLAD reloads per request and translates individual messages without conversation context or glossary guidance. It uses a 512-token context; messages that exceed the input limit fail without returning a partial translation. CPU inference is slower than a translation API, especially the larger model. No per-message API quota applies.
- **OpenAI-compatible endpoint:** Use your own local or hosted `/v1/chat/completions` server, model identifier, and optional key. Hosted services may charge for usage.
- **No translation:** Read and send original messages (called **Chat without translation** during first-run setup). The separate **Send original text** switch in Settings → Translation skips outgoing translation in Chatter and the reply panel while leaving incoming translation available. It does not bypass translation for native `/t` commands.
- **Local · lightweight (Argos):** Chatter downloads its private translation runtime during setup and language models as needed. Downloads require internet access and models can be large. Cached language pairs work offline. The lightweight worker runs locally without a listening network port. Hy-MT2 uses an authenticated loopback-only llama.cpp server owned by Chatter. MADLAD uses llama-completion through a private Windows pipe without a network port or source text files. Chatter stops its inference process when it exits.
- **DeepL API:** Enter an API key for the Free or Pro plan. Text sent for translation goes to DeepL and counts toward your API usage. A regular DeepL website subscription does not include an API key.
- **LibreTranslate:** Enter your server's `/translate` URL and optional API key. Text goes to that server; remote servers must use HTTPS.

You can keep two providers at once. The automatic provider translates new messages when auto-translate is on, and it translates outgoing messages when you preview or send. The manual provider is used when you click **Translate** on a message. For example, use a LibreTranslate server automatically and reserve DeepL for messages you choose to translate. A message that was translated automatically can be translated again with the manual provider.

Each conversation remembers the incoming language, the language you write in, the outgoing language, and whether auto-translate is on. Those choices are saved with your other settings. A conversation you have not customized follows the defaults from translation preferences. Under the message box, choose the language to translate from and the language to translate to. The swap button between them exchanges the two languages.

The language picker offers English, Polish, Russian, Korean, Japanese, Chinese, German, French, Spanish, Portuguese, Ukrainian, Hebrew, Arabic, and Turkish. The lightweight local provider prepares English pivot models for non-English pairs; availability depends on its model catalogue. Hosted providers can reject pairs their service does not support. Automatic detection can leave a message unchanged when its language is unrecognized or already matches the target. Results can vary, especially for short messages. Hebrew and Arabic are shown right to left.

## Game overlay

Turn on **Settings → Overlay → Show the game overlay** in Chatter and return to osu!stable in windowed mode. In menus and the lobby, the overlay follows the tab selected in the game and shows recent messages with their translations when auto-translate is enabled for that conversation. It hides when you switch to another app, minimize the game, pause reading or enter gameplay. Drag the overlay by its header, resize its edges, and scroll through retained messages with the mouse. Click Lock for mouse input to pass through to osu!, or use Ctrl+Shift+O to switch modes. Position, size, opacity and shortcuts are configurable in Settings → Overlay; **Reset overlay position** takes effect when saved. Windowed/borderless mode is required for the external overlay; native chat translation also works in exclusive fullscreen.

When the overlay shows the reply shortcut, press **Ctrl+Shift+Enter**. Type your message in the reply panel and choose its source/target languages. The panel no longer requires a command prefix. **Preview** only translates the draft. **Send** or **Enter** submits it once to the conversation named in the panel, using Chatter's existing sender.

## In-game translation

Turn on **Settings → In-game chat → Translate incoming messages in osu!** in Chatter to replace incoming message text inside osu!stable's own chat panel. Select the conversation in Chatter, then set **Settings → Translation → This conversation → Automatic translation** to **On** (or **Follow default** when the default is enabled) and choose its incoming language. The mode follows the tab selected in osu!, including when Chatter has another conversation selected. It uses the same automatic provider and remembered language settings as the app.

Choose Translation only, Translation + original, or Original only in Settings → In-game chat. Translations appear after the provider finishes. Names, timestamps, your own messages, and system lines keep their normal presentation. The standalone app and overlay remain available, and keep the original message text alongside translations. In-game translated lines are plain text; use Chatter for the original clickable links, or turn this mode off to restore them in osu!.

This mode works in menus and the lobby, including fullscreen, without an overlay window. Turning it off, pausing reading, disconnecting, entering gameplay, or closing Chatter restores original lines. If Chatter stops responding, originals return after the display lease expires (about three seconds, once osu!'s scheduler runs). It changes local render caches and does not send translated incoming messages to other players. Unsupported client layouts show an error in Chatter.

## Native outgoing commands

Enable **Settings → In-game chat → Enable /t and /translate commands**. In osu!'s normal chat box, type:

```text
/t en jestem developerem aplikacji osu-chatter
```

Chatter consumes the command locally, translates the message, and submits only the translation to the conversation captured when you pressed Enter. `/translate` is an alias. Supported target codes: `en pl ru ko ja zh de fr es pt uk he ar tr`. Normal game commands continue to work. The app’s translation status shows `/t ready` after the hook is active. When upgrading, close the older Chatter instance and run the new version; release-specific bridge identities prevent osu! from silently reusing old integration code.

Use `/t <target-code> <message>`; source detection is automatic. While the hook is active, malformed `/t` and `/translate` commands are also consumed locally and reported as errors. Nested slash commands are rejected. These aliases work only while their hook is active; check for `/t ready` before using them.

Commands require an active Chatter connection, a supported client, and menus/lobby. Translation expires after 45 seconds. Failure does not automatically retry a send; the original game draft is restored when the same account/channel is selected and its input remains empty. Cold model loading can make a command expire, so preview a translation in Chatter before using a large model for commands. Disabling commands, pausing, or closing Chatter cancels pending sends. The hook expires after missed heartbeats.

## Context, history and reading

Settings → Translation offers source-language hints globally and per conversation, automatic translation per conversation, and New only / Recent / All retained history policies. Recent defaults to 40 messages; All queues up to 200 recent messages, and visible older rows can be translated on demand. Originals remain visible when a message is skipped or a provider fails. Right-click a message to copy, retry, translate to another language, or open the player's profile.

Automatic work follows the game-selected conversation when native translation is enabled, the visible overlay conversation, and the conversation selected in Chatter. It prioritizes visible messages rather than translating every open tab in the background. The reader retains up to 200 messages per conversation; these are not a saved chat archive.

Local AI and chat-completions providers use six preceding messages by default, configurable from zero to 12, within three minutes and a 2,200-character bound. Later messages do not automatically rewrite earlier translations. Re-translate a line explicitly to retry with available context; an identical request can reuse the in-memory result. Argos, DeepL and LibreTranslate remain message-based. The **Terms and names to preserve** glossary is guidance for Local AI/chat-completions providers; URLs and clickable map/song titles are protected before translation across providers. Context improves some ambiguous messages but does not guarantee correct slang, misspellings, jokes, or sarcasm.

The modern transcript supports color emoji, text selection, RTL text, search, links and Jump to latest. It uses Microsoft Edge WebView2 in a private profile; a built-in renderer is available if the runtime is missing. **Settings → Application** controls the modern transcript, its message text size (11–24), Always on top, startup reading, private-message notifications while unfocused, and minimize-to-tray. Window position, size and maximized state are saved automatically. With minimize-to-tray enabled, minimizing or closing the window keeps Chatter running; use the tray menu's **Quit** to exit. Notifications do not include message text.

| Shortcut | Action |
| --- | --- |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous conversation. |
| Ctrl+, | Open Settings. |
| Ctrl+S | Save changes while Settings is open. |
| Esc | Leave Settings and discard unsaved edits, or close an idle reply panel. |
| Ctrl+F | Search the current conversation. |
| Ctrl+Backspace | Delete the preceding word in the composer. |
| Up in an empty composer | Recall the last submitted draft. |
| Enter | Translate and send the current draft once. |
| Ctrl+Shift+O | Lock / unlock a visible game overlay. |
| Ctrl+Shift+Enter | Open reply when the overlay permits it. |

The two overlay shortcuts are configurable in **Settings → Overlay** using a value such as `Control, Shift, Enter`. They are registered while the overlay is visible; another application's hotkey registration can prevent them from working. In interactive mode you can also click **Reply**.

Settings → Models lists installed lightweight language pairs and disk usage, pre-downloads pairs, and clears temporary download caches while retaining installed models. A private runtime still includes machine-translation dependencies; removing the LibreTranslate web-server stack does not make it a tiny download.

For Local AI, choose **Model storage folder**, **Local model preset**, and **Local engine**, then **Install recommended local engine + model**. Save the resulting paths and select Local AI in Translation. CPU is the default; Vulkan supports compatible AMD, Intel and NVIDIA GPUs with current Vulkan drivers. Installing Vulkan sets GPU layers to 100 (or keeps a previous nonzero value); CPU installation sets it to 0. Reduce GPU layers if memory is insufficient. Backend/preset choices take effect when you install; changing the choice alone does not replace the active paths. The status line shows CPU use, GPU requested but unverified, or GPU verified with the number of offloaded layers. Lightweight Argos always runs on CPU.

**Use existing / custom GGUF model…** selects a model from any drive. Chatter reads its GGUF metadata to select the MADLAD-compatible T5 or PLaMo prompt format even if the file was renamed; other custom models must follow translation instructions through chat completions. **Prepare engine for selected custom model** installs only the appropriate CPU/Vulkan engine while keeping the selected model. **Choose model storage folder…** changes where future AI downloads go. To use models already moved to another drive, select the folder and use the existing/custom model action, prepare its engine if needed, then save.

**Advanced model options** let you browse an existing engine and `.gguf` model, set CPU threads (1–32, default 2), and set GPU layers (0–100, default 0). Hy-MT2/custom chat models use `llama-server.exe`; MADLAD requires `llama-completion.exe` from the same engine distribution. MADLAD uses `<2target-language>` source prefixes; the preset downloads a llama.cpp-compatible [Q4_K_M conversion](https://huggingface.co/notjjustnumbers/madlad400-3b-mt-Q4_K_M-GGUF) of Google MADLAD-400, the same model family as [jbochi's Transformers conversion](https://huggingface.co/jbochi/madlad400-3b-mt). The Candle GGUF files in jbochi's repository use a different format and should not be selected here. PLaMo uses PFML3 prompts with context and glossary entries (comma/newline separated names to preserve, or `source => translation`). Chinese maps to Simplified Chinese; PLaMo does not list Ukrainian or Turkish, so those explicit pairs require another model. PLaMo is downloaded under the [PLaMo Community License](https://huggingface.co/pfnet/plamo-3-translate-2b-GGUF). Custom chat models must understand the translation prompt and chat-completions protocol. Selecting a different download folder does not move existing files. Leaving Settings cancels an active download; completed files remain installed even if you discard unsaved settings.

**Settings → About** shows the app version. **Inspect current osu! compatibility** copies a diagnostic snapshot to the clipboard without chat text. **Check for app updates** opens the releases page; it does not install an update automatically.

## Chat and privacy

Chat and translation text stay in memory; the translator caches up to 256 completed request results. The modern view uses an in-private WebView2 profile, with document navigation and external resources blocked. Local model downloads and runtime files are persisted; cloud translation sends messages to your chosen provider, and chat-completions providers also receive the selected context/glossary. Provider-side storage policies are controlled by that service.

Chatter reads conversations that are already open in osu!stable. It does not log in to osu!, join channels, or save chat text to disk. Selecting a conversation in chatter does not change the game's selected tab.

Profile pictures and supporter highlights come from the local osu! folder. Chatter reads `presence.db` for a player's id and supporter flag, then shows the picture already cached in `Data\a` when that file is present. If it is missing, chatter requests `https://a.ppy.sh/{id}` using only the numeric id. It does not send chat text or usernames, it does not call the osu! API v1 or v2, and it does not write avatars or presence data to disk. A name that is not in the local presence list is shown with a plain initial.

Sending is always a separate action. Chatter checks the selected account and target before sending; it won't submit commands, blank messages, or messages longer than 400 characters. Use the same Windows privilege level for chatter and osu!stable. Usually neither needs administrator access.

Translation providers receive text as described above. Automatic translation sends text to the automatic provider. Clicking Translate sends that message to the manual provider when one is set. Settings are kept under `%LOCALAPPDATA%/osu-chatter/settings.json`; API keys are protected for your Windows account with DPAPI.

Default local storage is under `%LOCALAPPDATA%/osu-chatter/`: `translator-v2` holds the lightweight runtime, dependencies and caches; `local-ai` holds the AI engine/models unless you choose another folder; `webview` holds WebView2 support data. Existing `translator-v1/data` language packages are reused when present. Clearing the lightweight download cache keeps installed language pairs and does not remove AI models. Closing Chatter releases its translation processes and in-memory chat; it does not remove downloaded files or settings.

## Troubleshooting

- If chatter cannot read the game, open osu!stable and leave the conversations you want to read open. If the game runs as administrator, run chatter the same way.
- If the modern transcript is unavailable, install Microsoft Edge WebView2 Runtime or use the built-in transcript in Settings → Application.
- The first local translation setup needs internet access. It downloads a Python runtime and dependencies; language models download when first used and may take several minutes.
- DeepL errors can indicate an invalid key, exhausted usage, or a plan mismatch. Check the API plan and key in translation preferences.
- If Local AI is unavailable, check the engine/model paths in Models, available disk space and RAM, and that you saved and selected Local AI in Translation. Warm it up with Preview before using timed native commands.
- If the overlay is missing, enable it, resume reading, bring osu!stable to the foreground in windowed/borderless mode, and leave gameplay. If its shortcut is occupied, change it or use the overlay's buttons in interactive mode.
- If sending is blocked, check that you are connected in a menu/lobby, the target conversation is still open and the translated text fits 400 characters. A missing outgoing echo does not trigger a resend; check the game before submitting again.
- If native integration fails after an osu! update, copy the About compatibility snapshot and consult the [compatibility guide](docs/stable-compatibility.md). Reading successfully does not establish that sending or native display works.

## License

osu-chatter is provided under the [PolyForm Noncommercial License 1.0.0](LICENSE). It permits noncommercial use, modification, and redistribution. Commercial use is not permitted under this license.
