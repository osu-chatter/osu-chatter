# Chat and in-game controls

For installation and your first conversation, start with the [README](../README.md#getting-started). Provider setup is covered in [Translation](translation.md).

## Reading and replying

Chatter shows channels and private conversations already open in osu!stable. Open new conversations in the game. Selecting a tab in Chatter doesn't switch the game's selected tab.

The chat view supports text selection, search, emoji, right-to-left text, player profiles and clickable links, including maps and songs in activity messages. Profile pictures and supporter highlights use osu!'s local presence data. Small animated avatars can play in the modern chat view; oversized animations use a still thumbnail.

Scrolling up keeps your reading position as new messages arrive. **Jump to latest** resumes following new messages. Right-click a message to copy it, retry translation, translate to another language or open the player's profile. Chatter retains up to 200 messages per conversation in memory; it doesn't save a chat archive.

Before replying, check the source and target languages below the message box. **Preview** translates without sending. **Send** or **Enter** translates and submits the reply once. The translated result must fit 400 characters. Chatter doesn't automatically retry sending after an error; check the game before submitting the same message again.

**Pause** stops reading and automatic translation, hides the overlay and stops in-game integration. **Resume** starts reading again. Sending requires a connection to osu!stable in menus or the lobby.

## Game overlay

1. Enable **Settings → Overlay → Show the game overlay** and save.
2. Put osu!stable in windowed or borderless mode and bring it to the foreground.
3. Stay in menus or the lobby. The overlay shows the conversation selected in the game.

Drag the header to move the overlay, resize its edges and scroll through messages. **Lock** makes mouse input pass through to osu!. **Ctrl+Shift+O** switches between locked and interactive modes.

Click **Reply** or press **Ctrl+Shift+Enter** when the shortcut is shown. The reply panel names the target conversation and has its own source/target language controls. Preview and send work as they do in the main window; no command prefix is needed.

Position, size, opacity and shortcuts are configurable in **Settings → Overlay**. **Reset overlay position** takes effect when you save. Shortcut values use a format such as `Control, Shift, Enter`; another app registering the same shortcut can prevent it from working.

The overlay hides when osu! loses focus, is minimized, enters gameplay, or Chatter is paused. Exclusive fullscreen needs [translation in osu!'s own chat](#translations-in-osus-chat) instead.

## Translations in osu!'s chat

1. Enable **Settings → In-game chat → Translate incoming messages in osu!**.
2. Select the conversation in Chatter and open **Settings → Translation → This conversation**.
3. Set **Automatic translation** to **On**, or **Follow default** if the default is enabled. Choose the incoming language and save.

Choose **Translation only**, **Translation + original** or **Original only** in **Settings → In-game chat**. Translations appear when the provider finishes. The mode follows the tab selected in osu!, even if Chatter has another conversation selected.

This works in menus and the lobby, including exclusive fullscreen. It changes the chat display on your PC; incoming translations aren't sent to other players. Names, timestamps, your own messages and system lines keep their usual presentation.

Translated lines in osu! are plain text. Use Chatter for the original clickable links, or turn the mode off to restore them in the game. Chatter and the overlay keep original text alongside translations.

Turning the mode off, pausing, disconnecting, entering gameplay or exiting Chatter restores the original lines. If Chatter stops responding, restoration normally happens after about three seconds, once osu!'s scheduler runs again. With minimize-to-tray enabled, use **Quit** in the tray menu to exit Chatter.

## Translating replies with /t

Enable **Settings → In-game chat → Enable /t and /translate commands**, save and wait for `/t ready` in Chatter. Then type a command in osu!'s normal chat box. These examples translate English into Russian:

```text
/t ru Hi everyone! How are you doing?
```

The longer `/translate` alias works the same way:

```text
/translate ru Thanks for the game! See you next time.
```

The format is `/t <target-language> <message>`. Chatter detects the source language and sends the translation to the conversation selected when you pressed Enter. In these examples, `ru` selects Russian as the target.

See [all supported languages and target codes](translation.md#languages). Your provider must support the chosen language pair. **Send original text** doesn't bypass translation for these commands.

Commands require an active Chatter connection and menus or the lobby. Check `/t ready` before using them: Chatter only handles these aliases while the integration is active. While active, malformed commands are consumed locally and reported as errors; nested slash commands are rejected. Other game commands work as usual.

A command expires after 45 seconds. For a large local model, run **Preview** in Chatter first to load it. On failure, Chatter restores the original draft when the same account and conversation are still selected and the input is empty. It won't overwrite a newer draft or retry the send automatically. Disabling commands, pausing or exiting cancels pending sends.

## Settings and window behavior

**Ctrl+,** opens Settings in the main window. **Save changes** or **Ctrl+S** applies edits. **Back to chat** or **Esc** discards unsaved edits and returns to your conversation and draft. Reading continues while Settings is open, and sections remember their scroll positions.

**Settings → Application** controls the modern chat view and its text size (11–24), Always on top, startup reading, private-message notifications and minimize-to-tray. Window position, size and maximized state are saved automatically. Notifications while unfocused don't include message text.

If **minimize-to-tray** is on, closing or minimizing the window keeps Chatter running. Use **Quit** in the tray menu to exit.

The modern chat view uses Microsoft Edge WebView2. A built-in renderer is available when that runtime is missing. **Settings → About → Check for app updates** opens the releases page; download and extract the new ZIP to update, closing the older Chatter instance first.

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous conversation |
| Ctrl+, | Open Settings |
| Ctrl+S | Save settings |
| Esc | Leave Settings without saving, or close an idle reply panel |
| Ctrl+F | Search the current conversation |
| Ctrl+Backspace | Delete the preceding word in the message box |
| Up in an empty message box | Recall the last submitted draft |
| Enter | Send the current draft, translating it unless disabled |
| Ctrl+Shift+O | Lock / unlock a visible overlay |
| Ctrl+Shift+Enter | Open the overlay reply panel when available |

The two overlay shortcuts are configurable and registered while the overlay is visible.
