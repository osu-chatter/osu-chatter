# osu-chatter 1.0.0

Read and translate osu!stable conversations in Chatter, over the game, or directly in osu!'s chat.

## Chat and translation

- Translate incoming messages and preview translated replies before sending.
- Use local Argos translation, Hy-MT2 Fast/Quality, MADLAD-400, PLaMo Translate, DeepL API, LibreTranslate or an OpenAI-compatible server.
- Choose separate providers for automatic translation and the Translate button, with language choices remembered per conversation.
- Give supported AI providers preceding messages and terms to preserve. Retry individual translations or choose another target language.
- Select and search chat text, follow links and open player profiles. The modern view supports color emoji and RTL text, with a built-in renderer available without WebView2.
- Adjust text size, keep the window on top, receive private-message notifications and minimize to the tray.

## In-game chat

- Move, resize and scroll an overlay, lock it for mouse pass-through, and reply from its reply panel.
- Show translations in osu!'s own chat, including exclusive fullscreen. Choose translation only, translation with the original, or original text.
- Translate replies from osu!'s chat box with `/t <language> <message>` or `/translate`.
- Inspect client compatibility in Settings → About to help diagnose integration errors after game updates.

## Installation

Extract the complete ZIP and run `chatter.exe`, keeping `chatter.exe.config` beside it. Requires Windows 10/11 x64, .NET Framework 4.8 and osu!stable. osu!lazer isn't supported.

Sending and native chat translation work in menus or the lobby. The overlay needs windowed/borderless mode. Local models download separately; hosted providers receive submitted text and may charge for usage. Chatter keeps chat text in memory rather than saving a chat archive.

See [Getting started](README.md#getting-started), [translation setup](docs/translation.md) and [troubleshooting](docs/troubleshooting.md). Dependency notices are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt); license terms are in [LICENSE](LICENSE).
