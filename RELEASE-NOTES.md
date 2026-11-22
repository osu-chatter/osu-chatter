# osu-chatter 1.0.0

osu-chatter is a Windows chat companion for osu!stable. Read open conversations, translate incoming messages and preview or send replies from the app, the optional game overlay or enabled in-game translation commands.

## Chat and translation

- Embedded settings with separate automatic/manual translation providers and remembered conversation languages.
- Lightweight local Argos translation, local Hy-MT2 Fast/Quality models, OpenAI-compatible chat-completions endpoints, DeepL API and LibreTranslate. Original-text chat mode is also available.
- Bounded preceding context and terminology guidance for AI providers, protected links/map titles, history policies, visible-message priority, per-message errors and manual retry.
- A selectable WebView2 transcript with color emoji, RTL text, search, player profiles and links, plus a built-in fallback renderer.
- Keyboard shortcuts, window placement, text size, topmost mode, optional private-message notifications and minimize-to-tray.

## Game integration

- A movable/resizable overlay with scrolling, mouse pass-through and a reply panel. External overlays require windowed/borderless mode.
- Incoming translations in osu!'s own chat, including exclusive fullscreen, with translation-only, bilingual and original presentation. Display leases restore originals when integration stops.
- Optional `/t <language> <message>` and `/translate` commands using the existing guarded sender. Sends are explicit and are never automatically retried.
- Shared client layout validation, release-specific bridge identities, version/fingerprint diagnostics and a separate upstream-version compatibility watch.

## Installation and limits

Extract the complete Windows ZIP and run `chatter.exe`, keeping `chatter.exe.config` beside it. Requires Windows 10/11 x64 and .NET Framework 4.8. Visual Studio, the SDK and system Python are not needed for a release. The modern transcript uses Microsoft Edge WebView2; the built-in transcript works without it.

This integration targets osu!stable x86, not lazer. Native operations and sending require menus/lobby and depend on the private client layout. Successful version detection is not a guarantee that every osu! build is supported. Models download separately, require free disk space and can mistranslate informal chat. Hosted providers require your configuration and may charge for usage.

Chatter does not save chat text to disk. Cloud translation sends text to the chosen provider; chat-completions providers can also receive the selected context/glossary. Settings and downloaded runtimes/models persist locally, and API keys use Windows DPAPI.

See [README.md](README.md) for usage and troubleshooting, [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) for dependency notices and [LICENSE](LICENSE) for the PolyForm Noncommercial terms.
