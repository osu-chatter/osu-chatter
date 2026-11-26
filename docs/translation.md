# Translation

Choose your provider and default languages in **Settings → Translation**, then **Save changes**. **Back to chat** or **Esc** leaves settings without saving your edits.

## Choosing a provider

| Provider | Setup |
| --- | --- |
| Local AI | Install a model and engine in **Settings → Models**, then select **Local AI** in Translation. See [installation](#installing-a-local-ai-model) below. |
| Local · lightweight | Uses Argos on your CPU. Chatter downloads its own runtime and the language pairs you need. Installed pairs work offline. |
| DeepL API Free / Pro | Enter an API key for the matching plan. A DeepL website subscription doesn't include API access. |
| LibreTranslate | Enter your server's `/translate` URL and optional API key. Remote servers must use HTTPS. |
| Chat API | Enter an OpenAI-compatible `/v1/chat/completions` URL, model identifier and optional key. This can be a local or hosted server. |
| No translation | Read and send original messages. This is called **Chat without translation** during first-run setup. |

Local providers need an internet connection for their initial downloads. Hosted providers receive the text you translate and may charge for usage; see [privacy](privacy.md#translation-providers).

### Automatic and manual translation

**Automatic engine** handles incoming messages when automatic translation is on, plus your outgoing drafts when you preview or send. **Translate button** chooses the provider for messages you translate manually. Leave it on **Same as automatic**, or choose another provider.

For example, you can use a local model automatically and DeepL only when you click **Translate**. You can translate a message again with the manual provider even if it already has an automatic translation.

To send original text while keeping incoming translations, enable **Send original text**. This applies to Chatter and the overlay reply panel; in-game `/t` commands still translate their text.

## Languages

The language controls below the message box set the language you write in and the language to send. The swap button exchanges them. Each conversation remembers these choices, its incoming language and whether automatic translation is enabled.

Set defaults in **Settings → Translation**. To override incoming translation for a conversation, select it in Chatter and use **This conversation** in those settings. New conversations follow the defaults until you customize them.

Chatter's language picker and `/t` commands support these 14 languages:

| Language | Code |
| --- | --- |
| English | `en` |
| Polish | `pl` |
| Russian | `ru` |
| Korean | `ko` |
| Japanese | `ja` |
| Chinese | `zh` |
| German | `de` |
| French | `fr` |
| Spanish | `es` |
| Portuguese | `pt` |
| Ukrainian | `uk` |
| Hebrew | `he` |
| Arabic | `ar` |
| Turkish | `tr` |

Use the code as the command's target, for example `/t ru Hello everyone!` to translate into Russian. See [command examples](usage.md#translating-replies-with-t).

Provider support varies. Argos may translate through English for non-English pairs; its available pairs depend on the model catalogue. PLaMo does not support explicit Ukrainian or Turkish pairs, and its Chinese target is Simplified Chinese.

If automatic source detection gets a short message wrong, set **Incoming source** for that conversation. Detection can leave a message unchanged when it can't identify the language or thinks it already matches your target.

## Installing a local AI model

1. Open **Settings → Models** and choose a **Model storage folder**.
2. Choose a **Local model preset** and **Local engine**.
3. Click **Install recommended local engine + model** and wait for the download to finish.
4. Select **Local AI** in **Settings → Translation**, then **Save changes**.
5. Try **Preview** on a draft to check that the model runs.

| Preset | Model download | Free disk space for setup |
| --- | --- | --- |
| Fast · Hy-MT2 1.8B Q4 | About 1.1 GB | At least 1.5 GB |
| Quality · Hy-MT2 7B Q4 | About 4.6 GB | At least 6.5 GB |
| MADLAD-400 · 3B Q4 | About 1.9 GB | At least 2.5 GB |
| PLaMo Translate · 2B Q4 | About 1.8 GB | At least 2.5 GB |

These figures describe downloads and disk space, not RAM requirements. Larger models take more memory and can be slow on CPU. Slang, jokes and misspellings can still be mistranslated.

Hy-MT2 stays loaded between requests. MADLAD reloads for each request and translates individual messages without conversation context or glossary guidance. Its 512-token input context can be too small for long messages; those requests fail rather than returning a partial translation. PLaMo supports context and glossary guidance and is distributed under the [PLaMo Community License](https://huggingface.co/pfnet/plamo-3-translate-2b-GGUF).

### CPU and GPU

**CPU** is the default engine. **Vulkan** can use compatible AMD, Intel and NVIDIA GPUs with current Vulkan drivers. Install the engine after choosing the backend; changing the picker alone doesn't replace the engine already selected.

Vulkan installation sets GPU layers to 100, or keeps your previous nonzero value. CPU installation sets them to 0. Reduce **GPU layers** if the model runs out of memory. The status line distinguishes CPU use, a GPU request awaiting verification and verified GPU offload. Argos always uses the CPU.

Under **Advanced model options**, you can choose 1–32 CPU threads (default 2) and 0–100 GPU layers (default 0).

### Existing models and another drive

Use **Use existing / custom GGUF model…** to select a model you already have. **Prepare engine for selected custom model** installs its engine without replacing the model. Save the resulting paths to use them.

Changing **Model storage folder** affects future downloads; it doesn't move existing files. If you move a model yourself, select it again with the existing/custom model action and prepare its engine if needed. Leaving Settings cancels an active download. Completed files remain installed even if you discard unsaved settings.

Chatter uses GGUF metadata to recognize MADLAD and PLaMo even after a file is renamed. Hy-MT2 and custom chat models use `llama-server.exe`; MADLAD requires `llama-completion.exe`. Other custom models need to understand Chatter's translation instructions through the chat-completions protocol.

The MADLAD preset uses a llama.cpp-compatible [Q4_K_M conversion](https://huggingface.co/notjjustnumbers/madlad400-3b-mt-Q4_K_M-GGUF). The Candle GGUF files in [jbochi's conversion](https://huggingface.co/jbochi/madlad400-3b-mt) use a different format and won't work here. PLaMo uses PFML3 prompts; glossary entries can be comma/newline-separated names or `source => translation` pairs.

### Lightweight language pairs

**Settings → Models** also lists installed Argos language pairs and disk use. Use **Pre-download lightweight language pair** to prepare a pair before going offline. Non-English pairs also prepare English pivot models where available.

**Clear lightweight download cache…** removes temporary downloads while keeping installed language pairs. It doesn't remove AI models. The lightweight runtime includes Python and translation dependencies, so its first download can still be large.

## History and context

**Existing messages** controls which retained messages get translated: new messages only, recent history, or all retained history. Recent history defaults to 40 messages; all retained history queues up to 200. Chatter prioritizes visible messages in the app, overlay and native chat rather than translating every open tab in the background. Older visible rows can be translated on demand.

AI providers can use preceding messages to help interpret a line. **Context messages** defaults to six and can be set from zero to 12. Context is bounded to three minutes and 2,200 characters. Argos, DeepL, LibreTranslate and MADLAD translate messages individually.

**Terms and names to preserve** provides glossary guidance for AI providers that support it. Links and clickable map/song titles are protected across providers. An identical request can reuse an in-memory translation; later messages don't automatically revise earlier results. Right-click a line to retry or translate it to another language.

For provider errors or slow startup, see [troubleshooting](troubleshooting.md#translation-fails-or-is-slow).
