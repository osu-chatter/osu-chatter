# Privacy and local storage

Chatter reads conversations already open in osu!stable. It doesn't log in to osu!, join channels or save chat text to disk. Retained messages and translation results stay in memory and are cleared when Chatter exits.

## Translation providers

Local AI and Argos translate on your PC. Their engines and models need internet access for installation. Argos uses a local worker without a listening network port. Hy-MT2 and chat-style local models use a Chatter-owned llama.cpp server authenticated on loopback; MADLAD uses a private Windows pipe. Chatter stops its translation processes when it exits.

DeepL, LibreTranslate and Chat API receive the text submitted to the configured service. A server on another machine receives that text even if you run the server yourself. AI providers that support context and glossary guidance can also receive the selected preceding messages and glossary. Provider-side storage depends on that service's policies.

Automatic translation uses **Automatic engine**. Clicking **Translate** uses the **Translate button** provider when one is configured. [Translation settings](translation.md#automatic-and-manual-translation) explains how to choose them separately.

## Chat display and profile pictures

The modern chat view uses a private WebView2 profile. Its document blocks external navigation, external resources and browser permissions. Chatter caches up to 256 completed translation results in memory.

Player IDs and supporter flags come from `presence.db` in the local osu! folder. Chatter first looks for avatars in osu!'s `Data\a` cache. If an avatar is missing, it requests `https://a.ppy.sh/{id}` using the numeric player ID. Those requests contain no chat text or usernames. Chatter doesn't use the osu! API v1/v2 or write avatar or presence files to disk. Names absent from the local presence list get an initial instead of an avatar.

Private-message notifications don't include message text. **Inspect current osu! compatibility** in About copies version and integration diagnostics without chat text.

## Files stored on your PC

The default storage folder is `%LOCALAPPDATA%/osu-chatter/`.

| Location | Contents |
| --- | --- |
| `settings.json` | Preferences, conversation language choices and API keys protected for your Windows account with DPAPI |
| `translator-v2/` | Argos runtime, dependencies, language models and download caches |
| `local-ai/` | Local AI engines and models, unless you choose another folder |
| `webview/` | WebView2 support data |

Existing language packages in `translator-v1/data` are reused when present. **Clear lightweight download cache…** keeps installed language pairs and doesn't remove AI models. Changing the AI model folder affects future downloads; it doesn't move existing files.

Exiting Chatter leaves downloaded files and settings on disk. With minimize-to-tray enabled, use the tray menu's **Quit** to exit fully.
