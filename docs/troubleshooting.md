# Troubleshooting

## Chatter can't read the game

Start osu!stable, sign in and open the conversations you want to read. osu!lazer isn't supported. Chatter and osu! should run at the same Windows privilege level; usually neither needs administrator access.

If reading was paused, click **Resume**. If the problem started after an osu! update, see [integration errors](#in-game-integration-fails-after-an-osu-update).

## The chat view is missing or looks different

The modern chat view needs Microsoft Edge WebView2 Runtime. Install that runtime or use the built-in renderer under **Settings → Application**.

For text size, use **Message text size** in the same section. Click **Jump to latest** if the view is staying on older messages after you scrolled up.

## Translation fails or is slow

For **Local AI**, check that both the engine and model paths in **Settings → Models** point to installed files. Select **Local AI** in **Settings → Translation** and save. Installing a model alone doesn't select the provider.

The first request loads the model and can take longer. Use **Preview** to check it before using `/t`, whose requests expire after 45 seconds. Larger models can be slow on CPU; if Vulkan runs out of memory, reduce GPU layers. MADLAD reloads for each message and rejects text that exceeds its input context. See [local model setup](translation.md#installing-a-local-ai-model).

For **Local · lightweight**, the first setup downloads a private Python runtime and translation dependencies. Language pairs download as needed and may take several minutes. Prepare them ahead of time under **Settings → Models** if you need offline translation.

For **DeepL**, check the key, API quota and Free/Pro plan selection. A website subscription doesn't provide an API key. For **LibreTranslate** or **Chat API**, check the server URL, key and, for Chat API, the model identifier.

If a message remains untranslated, check automatic translation for that conversation. An unknown source language or text already matching the target can be left unchanged. Try an explicit incoming source language or right-click the message to translate it manually. The original text stays visible when translation fails.

## The overlay doesn't appear

Enable it under **Settings → Overlay**, save, resume reading and bring osu!stable to the foreground in windowed or borderless mode. Stay in menus or the lobby. The overlay hides during gameplay and when the game loses focus.

Use **Reset overlay position** and save if it may be off-screen. If a shortcut is occupied by another app, change it in Overlay settings or use **Lock** and **Reply** in interactive mode.

## Translations don't appear in osu!'s chat

Enable **Translate incoming messages in osu!** under **Settings → In-game chat** and save. Automatic translation must also be on for that conversation, with the desired incoming language selected. The feature follows the game-selected tab and works in menus or the lobby.

Choose **Translation only** or **Translation + original**; **Original only** leaves the original display. If it still fails, check Chatter's error message and [integration diagnostics](#in-game-integration-fails-after-an-osu-update).

## Sending is blocked or /t isn't ready

Check that you're connected in menus or the lobby, the target conversation is still open, and the translated result fits 400 characters. Shorten the draft if necessary. A missing outgoing message echo doesn't cause an automatic resend; check osu! before sending again.

For `/t`, enable **/t and /translate commands** in **Settings → In-game chat**, save and wait for `/t ready`. Close any older Chatter instance after upgrading. See [command usage](usage.md#translating-replies-with-t) for timeouts and draft recovery.

## In-game integration fails after an osu! update

Reading, sending, native display and `/t` depend on different parts of osu!stable's internal layout. One can work while another fails.

Open **Settings → About → Inspect current osu! compatibility** to copy a diagnostic snapshot. Include it with the Chatter version, Windows version and steps to reproduce in a [bug report](https://github.com/osu-chatter/osu-chatter/issues). Check screenshots and other attachments for private chat or API keys before posting. The compatibility snapshot itself contains no chat text.

For maintaining the integration, see the [technical compatibility guide](stable-compatibility.md).

## Closing the window doesn't stop Chatter

When **minimize-to-tray** is enabled, closing the window keeps the app running. Choose **Quit** from the tray menu to exit, or turn the option off in **Settings → Application**.
