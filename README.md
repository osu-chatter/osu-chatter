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

osu-chatter lets you read and translate osu!stable chat in a separate window, an overlay, or directly in the game's chat. You can also translate your replies before sending them.

- Read channels and private conversations you have open in osu!.
- Translate messages locally on your PC, or use DeepL, LibreTranslate or an OpenAI-compatible server.
- Keep chat visible over the game, or show translations in osu!'s own chat panel, including fullscreen.
- Search messages, copy text, open player profiles and follow beatmap links.

## Download

[Download the latest release](https://github.com/osu-chatter/osu-chatter/releases/latest), extract the whole ZIP and run `chatter.exe`. Keep `chatter.exe.config` beside it.

You need **64-bit Windows 10 or 11**, **.NET Framework 4.8** and **osu!stable**. osu!lazer is not supported **YET**. Release builds don't need Visual Studio, the .NET SDK or a separate Python installation.

## Getting started

1. Start osu!stable, sign in and open a channel or private conversation in the game.
2. Start Chatter and choose a translation provider and languages. To use Local AI, install a model in **Settings → Models**, then select **Local AI** in **Settings → Translation** and save. You can also choose **Chat without translation** during setup.
3. Select a conversation in Chatter. Turn on automatic translation to read incoming messages in your chosen language.
4. Type a reply and check the source and target languages below the message box. **Preview** translates the draft; **Send** or **Enter** sends it. Translated replies must fit osu!'s 400-character limit.

Open new channels and private conversations in osu!. Chatter reads the conversations already open there. **Pause** stops reading and in-game integration; **Resume** starts reading again.

See [translation and local models](docs/translation.md) for setup details and the [supported languages](docs/translation.md#languages) for language names and command codes.

## Using Chatter in-game

Enable the **game overlay** in **Settings → Overlay**. It follows the conversation selected in osu! while you're in menus or the lobby. Use windowed or borderless mode; **Ctrl+Shift+O** locks or unlocks the overlay, and **Ctrl+Shift+Enter** opens its reply panel when available.

For translations in **osu!'s own chat**, enable **Translate incoming messages in osu!** under **Settings → In-game chat**. Automatic translation must also be on for that conversation. This works in exclusive fullscreen too.

You can enable **/t and /translate commands** in the same section. Wait for `/t ready` in Chatter, then type a command in osu!'s chat box. For example, translate an English greeting into Russian:

```text
/t ru Hi everyone! How are you doing?
```

Or ask about a multiplayer game:

```text
/t ru Does anyone want to play multiplayer?
```

The source language is detected automatically; `ru` selects Russian as the target. See [more command examples](docs/usage.md#translating-replies-with-t).

Sending and in-game translation work in menus and the lobby. Game updates can break the integration. See the [chat and in-game guide](docs/usage.md) for controls and limitations, or [troubleshooting](docs/troubleshooting.md) if something isn't working.

## Privacy

Chatter keeps chat text in memory and doesn't save a chat archive. Local translation runs on your PC; hosted translation sends text to the provider you choose. AI providers may also receive preceding messages as context. [Privacy and local storage](docs/privacy.md) explains what is sent and stored.

## Building

Install the .NET 10 SDK and .NET Framework 4.8, then run:

```powershell
.\run.bat -NoPause
```

If you get `MSB3644`, install the .NET Framework 4.8 Developer Pack. See [Contributing](CONTRIBUTING.md) for checks and pull requests, and the [documentation index](docs/README.md) for development and release guides.

## License

[PolyForm Noncommercial 1.0.0](LICENSE). Noncommercial use, modification and redistribution are permitted; commercial use is not permitted under this license.

**osu-chatter** is an **unofficial** community project and is not affiliated with osu! or its developers.
