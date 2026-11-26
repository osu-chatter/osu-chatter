# osu!stable compatibility and maintenance

This is the maintainer reference for the memory reader, in-process bridge and native chat integration. For app controls, see [Chat and in-game controls](usage.md); for diagnosing a failure, start with [Troubleshooting](troubleshooting.md).

Chatter targets the x86 osu!stable client. It validates signatures, object layouts and reflected members at runtime. The recorded test baseline identifies one executable, not a list of supported builds.

## Address discovery

The app does not depend on a saved list of absolute process addresses. It finds the chat root and account/state storage using byte signatures, then follows pointers and relative field offsets. Actual addresses belong to the running process and can differ between launches; this alone does not require changing the integration. Signatures, object layouts and reflected member names are separate assumptions that may change with a game build.

Game-specific signatures, offsets and obfuscated names are centralized in `StableCompatibilityProfile.cs`; x86 CLR layout constants are in `Clr32Layout.cs`. Runtime checks are local to the running client; the separate [compatibility watch](#stable-compatibility-workflow) checks upstream version metadata.

The inventories below describe the source assumptions used by the integration. Update them alongside the shared profile when a client layout changes.

## Updating the integration

The game-specific values have one edit location: [`src/Shared/StableCompatibilityProfile.cs`](../src/Shared/StableCompatibilityProfile.cs), source-linked into both Core and Bridge. `Chat` contains the root signature and chat field offsets; `Account` contains account/state signatures, operand offsets, profile fields and login values; `Members` contains in-process type/member names and invocation flags. The allowed menu/lobby states live in the same profile. There is one current profile and no runtime build allowlist. `NativeChat` also contains renderer/cache and scrolling member names.

[`Clr32Layout.cs`](../src/Chatter.Core/Clr32Layout.cs) separately names x86 CLR string/list/array and primitive layout assumptions. Keep these separate from osu! field changes. `OsuChat` handles process/discovery/retries, `StableAccountReader` decodes fresh identity/state observations, `ChatGraph` decodes chat, and `StableGame` resolves and invokes game members. UI and translation code do not consume offsets. Existing public `ChatRules` login/state helpers forward to the profile for source compatibility.

When updating a layout, identify the changed fields/signatures/members on the actual client, edit the shared profile, and update independently specified fixtures in `Chatter.Tests` to represent that observed layout. Run `check.bat -NoPause`, then perform the actual-client checks below before claiming compatibility. Rebuild the entire solution so the reader and embedded bridge use the same values. Do not update CLR offsets or weaken validation simply to make a broken game layout pass.

Failed account reads produce a fresh unavailable identity, and an unreadable login produces unknown connection status; neither reuses prior send authority. Discovery requires one structurally valid chat root and rejects ambiguous account signature operands. Native display/command operations extend the authenticated pipe without changing the existing send commit handshake.

## Stable compatibility workflow

[osu!stable update watch](../.github/workflows/stable-compatibility.yml) runs daily at 08:00 UTC and can be started manually in GitHub Actions. Scheduled runs require the workflow on the default branch, so a local file alone does not activate monitoring. See [GitHub scheduling](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#schedule).

The monitor validates [stable-profile.json](stable-profile.json) against the source profile identifier/hash, fetches the [public changelog listing](https://osu.ppy.sh/home/changelog?stream=stable40) with `Accept: application/json`, and compares its latest build with the manually established `osu_build`. It first runs offline fixtures; ordinary `check.bat` also runs those fixtures without contacting the release feed.

| Result | Meaning |
| --- | --- |
| Matching version | The recorded upstream version is unchanged; no live functionality was tested. |
| `review-required` | The upstream version differs. Check reading, sending, native display/restoration and commands separately before claiming compatibility. A version change does not by itself prove breakage. |
| `baseline-unverified` | The inspected executable's official build number is unrecorded. Establish it before updating `osu_build`. |
| `fetch-failed` / `feed-invalid` | Update metadata could not be verified. Compatibility is unknown, not automatically broken or working. |
| `manifest-invalid` / `profile-mismatch` | Recorded metadata is invalid or disagrees with the source profile. |

The watch detects upstream version changes. Reading, sending, native display and commands still need separate game checks. It is independent of runtime validation and the required build check.

The job reads repository contents and has `issues: write` to open review issues. It never launches/downloads the game, sends chat, changes offsets or the baseline, or publishes releases. Keep it separate from the required build check, because its result depends on the external feed and manual compatibility evidence.

### Automatic review issues

When the live check detects a different upstream version, [report-stable-update.ps1](../scripts/report-stable-update.ps1) creates an issue titled `osu!stable <version>: integration review required`. It shows the recorded and detected build, three checks to complete, and links to the changelog, workflow run and profile. The linked profile contains the executable fingerprint and other baseline data.

An invisible marker identifies each upstream version. Before posting, the script checks all pages of open and closed issues. An existing issue prevents duplicates and further comments, even after closure. Workflow concurrency serializes scheduled and manual runs to prevent competing creations.

The check still fails while versions differ, including after opening or finding an issue. Fetch failures, invalid feed/profile data and an unverified baseline don't create version-review issues. Saved changelog fixtures are also blocked from publishing. GitHub API errors fail the workflow without retrying the issue write automatically.

To preview an issue locally, create a report with `check-stable.ps1 -ChangelogPath <saved-response.json> -ReportPath <report.json>`, then run `report-stable-update.ps1 -ReportPath <report.json> -Repository osu-chatter/osu-chatter -DryRun`. Add `-OutputPath <preview.md>` to save its Markdown. Preview mode makes no GitHub requests; the checker still reports a version mismatch as an error after writing its report.

Run `./scripts/check-stable.ps1` for the live metadata check or `./scripts/test-stable-monitor.ps1` for its offline fixtures. `-ChangelogPath <saved-response.json>` supports offline diagnosis and explicitly identifies fixture input. After validating an actual client build, update the official identifier and fingerprint/evidence together; do not weaken integration checks to accommodate an unknown layout.

## Reading the installed game version

`StableVersionReader` reads the exact `LastVersion` key from the live configuration dictionary, then falls back to `osu!*.cfg` next to the running executable. The diagnostic reports a build such as `b 20261118.1`, its source and the configured release stream separately from PE `FileVersion` (`1.3.3.8`). Configuration-only results describe the last launched version and can be stale; conflicting per-user files remain unknown instead of picking one arbitrarily. `LastVersionPermissionsFailed` is never used as the current build.

The version-reader memory assumptions are in `StableCompatibilityProfile.ClientVersion`:

| Item | Current assumption |
| --- | --- |
| Configuration signature | `8D 45 EC 50 8B 0D ?? ?? ?? ?? 8B D7 39 09 E8 ?? ?? ?? ?? 85 C0 74 ?? 8B 4D EC` |
| Operand extraction | Match `+6`; read operand -> static slot -> configuration dictionary |
| Dictionary entries / count | Array pointer `+8`; count `+0x1C`, limited to 1–4096 and checked against array capacity |
| Array entries | CLR array data starts at `+8`; each entry occupies `0x10` bytes |
| Entry key / bindable | Key string pointer at `+0`; bindable pointer at `+4` |
| Bindable string | Value string pointer at bindable `+4`; decode using the existing CLR string layout |
| Version key / format | Exact `LastVersion`; normalize `b20261118.1`, `b 20261118.1` or `20261118.1`, validating the calendar date |
| Mutation checks | Re-read operand, dictionary, array, count and selected entry/value pointers before accepting the build |

Only the explicit Settings → About compatibility inspection performs the extra signature scan. It runs in the background, outside settings startup and normal chat polling. Version metadata is diagnostic and never grants permission to send. Failure falls back to configuration files without weakening account/chat checks. Independent fixtures cover the memory structure.

## Current paths

Reading: `MainForm.StartPoll` -> `ChatService.Read` -> `OsuChat.Read` -> `Prepare` -> `RefreshProfile` -> `ChatGraph.Read` -> `ChatSnapshot`.

`Prepare` finds the first process named `osu!.exe`, opens a read/query handle, scans executable memory for candidate chat roots, and accepts exactly one distinct root whose graph can be read. `RefreshProfile` separately scans for account, login, and game-state addresses and delegates account decoding to `StableAccountReader`. `Read` retries inconsistent graphs; after three failures it discards the root. Discovery retries are throttled to five seconds. A dead process clears the root, signature addresses, identity, and retry state before attachment.

Sending: `ChatService.Plan` captures account/PID and an open target -> translation -> `Send` rechecks identity and target -> `BridgeClient.Request` attaches the managed bridge if needed -> `Entry.Start` constructs `StableGame` -> `Session.Serve` schedules work -> `StableGame.Validate` -> commit -> a second `Validate` -> `StableGame.Send`.

The bridge invokes the game's own sender on its scheduler. The reader does not write chat structures. The optional native display adapter assigns only message render-cache references; it never assigns original message bodies, senders, timestamps, or markup. Sending does not change the selected tab. After acknowledgment, `ChatService` waits 150 ms and checks for a new matching outgoing echo; absence of an echo does not trigger a resend.

## Compatibility inventory

Offsets below are bytes, including the x86 CLR method-table word where applicable. They describe source assumptions, not a verified build specification.

### Discovery and account (`OsuChat.cs`, `StableAccountReader.cs`, `ProcessMemory.cs`)

| Item | Current assumption |
| --- | --- |
| Process identity | First case-insensitive `osu!.exe` match; rejects non-x86 processes on 64-bit Windows. No build/stream allowlist; fingerprint is diagnostic |
| `statusPtr` | Signature `48 83 F8 04 73 1E`, result at match `-0x4` |
| `userProfilePtr` | Signature `FF 15 ?? ?? ?? ?? A1 ?? ?? ?? ?? 8B 48 54 33 D2`, result at match `+0x7` |
| `rawLoginStatusPtr` | Signature `B8 0B 00 00 8B 35`, result at match `-0xB` |
| Account traversal | Read word at signature result -> static slot -> profile object |
| Account fields | Name pointer at profile `+0x30`; signed account ID at `+0x70` |
| Account string | Length at `+4`, UTF-16 data at `+8`; requires 1–1000 code units; trims trailing NULs |
| Login traversal | Read word at signature result -> static slot -> raw login value |
| State traversal | Read word at signature result -> static address -> signed 32-bit state |
| Login values | `ChatRules.LoginConnected = 65793`; `LoggedIn` rejects `256`, nonpositive IDs, and the trimmed name `guest` (case-insensitive) |
| Allowed state values | `StableCompatibilityProfile.MenuStates = { 0, 5, 7, 11, 12, 13, 14, 15, 17, 18 }`; all other or unreadable states block sending |

`LoggedIn` alone does not establish connection: `Identity` additionally requires login exactly `65793`, a positive account ID, and an allowed state. `Connected` is derived from that exact login value when profile refresh succeeds.

`ProcessMemory.Scan` scans all matching sites for each named pattern and reads their storage operands. Repeated sites resolving to the same operand are accepted; distinct operands raise `AmbiguousSignature`. Unreadable or out-of-range operands are skipped. The first accepted site is retained for decoding. It scans committed readable regions (read-only, read/write, execute/read, execute/read/write), skips regions over 100 MiB, and skips non-image, non-executable regions over 1 MiB. Chunks advance by 64 KiB and include the longest pattern as overlap. Result addresses must be positive and below `uint.MaxValue`.

`VisitExecutable` scans committed execute/read or execute/read/write regions, skips regions over 32 MiB, and uses 64 KiB chunks with 128 bytes of overlap. Both scans stop on failed region queries, zero region sizes, or address wraparound. These are scanner/resource safeguards, not evidence of a supported build. `ReadBytes` requires a nonzero handle and address, size from one byte to 8 MiB, and a complete successful OS read.

### Chat graph (`ChatGraph.cs`)

| Structure | Current layout or signature |
| --- | --- |
| Root code | `8B 0D ?? ?? ?? ?? 8D 57 04 E8 ?? ?? ?? ?? B8 ?? ?? ?? ?? 89 47 0C 8B D7 ?? ?? ??` |
| Root extraction | Absolute little-endian address at match `+2`; scanner requires 27 available bytes, although the final checked byte is at `+23` |
| Root bounds | Address `>= 0x10000`, `< 0x7fff0000`, divisible by four |
| Root-relative statics | All-room list pointer at `+0`; selected-room pointer at `-20`; tab-manager pointer at `+4` |
| Tab manager | Tab-list pointer at `+0x1C` |
| Tab | Room pointer at `+12` |
| Room | Method table at `+0`, name pointer at `+4`, message-list pointer at `+16` |
| Message | Body pointer at `+4`, sender pointer at `+8`, markup-object pointer at `+16`, eight-byte local `DateTime` at `+36` |
| Markup object | Original-text pointer at `+12`. Used only when removing link markup equals the display body. A missing pointer, or text that does not match, leaves the message plain |
| CLR list | Array pointer at `+4`, count at `+12`, mutation version at `+16` |
| CLR array | Capacity at `+4`, four-byte items from `+8` |
| CLR string | Length at `+4`, UTF-16 data at `+8` |

Graph checks and interpretation to preserve:

- Require memory and root `>= 20`; require a readable, nonempty all-room list (maximum 512). The first room's method-table word must be readable and `>= 0x10000`.
- Require readable selected-room and manager words and a valid tab list (maximum 64). Tabs with an unreadable/null room pointer or a room method table different from the first room are skipped.
- Require nonempty room names of at most 160 code units with no control characters, valid message lists (maximum 10,000), at least one resulting channel, and at least one selected channel.
- Lists require an address `>= 0x10000`, readable array/count/capacity/version, count no larger than its limit or capacity, and capacity at most 65,536. Read only the last 200 messages per room.
- Graph strings require address `>= 0x10000`, readable length/data, at most 4096 code units, and valid surrogate pairing; empty strings are allowed. `Word` requires exactly four bytes.
- Timestamp data must be readable; mask with `0x3fffffffffffffff`, then require ticks in `600000000000000000`–`3155378975999999999`. Display local time-of-day using 10,000,000 ticks/second and 86,400 seconds/day.
- Empty body or sender denotes a system message. An empty body displays the sender text as the body. When a sender label starts with the matching `HH:mm ` prefix, strip that prefix, then one trailing colon, then one leading `*`. The `*` marks an action line such as playing or listening. It is not a supporter flag. Compare own names case-insensitively after replacing underscores with spaces. Names starting with `#` are channels; others are private conversations.
- When message +16 addresses an object (`>= 0x10000`), read the original text from that object's +12 and re-read both pointers. If either pointer changes, reject the snapshot. Keep the original text only when its visible form equals the display body. A markup pointer below `0x10000` leaves the message plain and does not fail the read. System messages stay plain.
- Re-read body/sender pointers after reading each message. Recheck list array/count/version, room message-list pointer, root all-room/manager pointers, manager tab-list pointer, and selected-room pointer before returning. These checks reject observed concurrent mutation; they do not provide an atomic snapshot or identify a game build. The markup pointer is re-read only when it addresses an object, so a zero word is read once.
- Message IDs use ticks, channel, processed display sender, and text in an FNV-style hash plus a duplicate occurrence index. Keep IDs independent of object addresses and keep rendering/unread behavior stable during extraction.

### In-process names (`Chatter.Bridge/Bridge.cs`)

All lookups include public and nonpublic members. `StableGame` first requires the entry assembly's filename to be `osu!.exe` (case-insensitive).

| Logical member | Exact name | Current lookup/contract |
| --- | --- | --- |
| Chat type | `#=zfZ67dsuuO$fV485iSEUJubF$6dHYUCshjA==` | Entry assembly type |
| Game type | `#=zUp$mEEXw9tnX4wTw9g==` | Entry assembly type |
| Bancho type | `#=zjlzdmSZWCm7Gt_hCRy1j458=` | Entry assembly type |
| Room type | `#=zxm_sYRQOWDUquMAvmG8d3r1mUahY` | Entry assembly type |
| Send method | `#=zk23iJUA=` | Chat static method `(string, string, bool) -> void`, nonnull method body; invoked with `(text, target, false)` |
| Current user | `#=zZPBGk$8=` | Game static field |
| User ID | `#=zOE3Zzjk=` | Instance field on current-user field type; must be `int` |
| Mode | `#=zPlM2ZxE=` | Game static field; value converted to `int` |
| Connected | `#=z2yQx9JE=` | Bancho static field; declared type must be `bool` |
| Ready | `#=zpKhmMXy_rJS1` | Bancho static field; declared type must be `bool` |
| Rooms | `#=znhMY61j4uPK$oUhrPw==` | Chat static field; declared type must implement `IList` |
| Room name | `#=z$Skj1XQ=` | Room instance field; declared type must be `string` |
| Scheduler | `#=zxC1wkg8=` | Game static field; runtime value must be nonnull |
| Schedule method | `#=zmX6lT8Y=` | Exactly one matching instance method with two parameters, second `bool`; return type must be `bool`; invoked with `(callback, true)` |
| Callback | First schedule parameter type | Must be a delegate with `Invoke() -> void` |

Required types/fields must exist; reflection errors prevent bridge startup. Construction explicitly checks the sender return/body, scheduler return and callback contract, user-ID type, connected/ready booleans, room-name string and room-list interface. The current-user and scheduler values must be usable at runtime; the mode is converted to an integer during validation. Native renderer contracts are checked separately when native display is first used.

Every scheduled validation requires a valid target, positive requested account, matching current user ID, connected and ready flags, an allowed game state, and a current room list of at most 512 containing the exact target name. A malformed room/member access also fails the operation. Validation runs both before granting commit and immediately before send.

### Sending safeguards outside the layout

These stay in the existing service/transport/rule boundaries:

- `ChatService` checks PID/account equality across translation, target membership and snapshot PID, validates text, and serializes check/send operations with an interlocked gate.
- `SharedChatRules` limits text to 400 UTF-16 code units, rejects blank/control-containing text and a leading slash after leading whitespace; target rules reject blank/control-containing names, names over 160 units, `#userlog`, `#highlight`, and leading `/`. Those target conventions should be documented alongside the adapter if they change, while message validation remains shared policy.
- `BridgeClient` verifies the extracted bridge bytes against its bundled/sidecar source, resolves exports relative to actual modules, checks allocation/write/protection/thread results, and bounds startup (8 seconds, then up to 3 seconds connecting). It retains remote allocations if the startup thread may still be running.
- Startup options require four lines, a `chatter-` pipe name of at most 100 letters/digits/hyphens, a 32-byte hex key, and positive host PID. Host start time is parsed and used to detect host replacement. Invalid options return 10; missing types/members return 12, invalid operations return 13, and other startup failures return 11.
- Pipe endpoints verify the peer PID; the server restricts its ACL to the current Windows user and checks host PID plus start time. The 32-byte random key is compared without an early exit. The key is cleared when the session ends.
- Protocol magic is `0x52424331`; operations are check `0`, send `1`, local display update `2`, restore display `3`, command polling `4`, or command stop `5`. The header is 45 bytes (key at 4, operation at 36, account at 37, target length at 41). Strict UTF-8 and byte bounds (target 1–640, text 0–1600 for check/send) precede rule validation; check requests require empty text. Display payloads have separate limits below.
- Scheduled work expires after 2 seconds and checks cancellation, host liveness, and session stop state. A send requires a matching eight-byte nonce plus an unexpired deadline no more than 100 ms ahead; the client grants 75 ms. The server waits 100 ms for commit, bounds initial reads to 2 seconds, response writes to 200 ms, and scheduled completion to 2300 ms. Client request/status I/O is bounded to 2300 ms and commit/result I/O to 200 ms. Pipe acceptance waits at most 60 seconds.
- Response bytes are ready `1`, sent `2`, check/display passed `3`, rejected `4`. Transport failure after commit reports uncertainty and does not automatically retry a message.

`Directory.Build.props` makes the app x86 and the projects target .NET Framework 4.8. `ClrStub.cs` depends on x86 calling conventions and CLR v4 hosting (`clr.dll`, `mscoree.dll`, `CLRCreateInstance`, `v4.0.30319`; hosting COM slots 3/5/9/11 and Release slot 2). `NativeMethods.cs` owns Windows access flags and APIs. These are platform/runtime assumptions to validate and keep separate from per-build osu! names and offsets. Bridge entry names, wire offsets/magic, and COM offsets are not game layout constants.

## Native chat display integration

`MainForm.UpdateNativeChat` uses the existing translation results for the tab selected **in osu!** and refreshes a display batch approximately once a second while fresh reading is enabled. `KickAuto` prioritizes that tab when native mode is on, then continues translation for the overlay/app conversation. All modes share per-conversation language, provider results, and message IDs. `NativeChatTranslations.Ready` takes only incoming, non-system rows from the last 200 messages, normalizes CR/LF/tab to spaces, rejects invalid/control-containing translations, and bounds the complete batch. `ChatGraph` and the bridge source-link the same `ChatMessageIdentity` implementation; IDs still use original text and never include addresses or translated render text.

`ChatService.DisplayTranslations` verifies snapshot PID/account, fresh identity and target membership, then `BridgeClient.Display` uses the existing authenticated pipe. Operation `2` carries the normal account/target header followed by a binary display payload: a little-endian Int32 row count, then pairs of BinaryWriter length-prefixed strict UTF-8 strings (message ID and translation). At most 200 rows, 40 ID characters, 4096 UTF-16 translation units, and 262144 payload bytes are accepted. Duplicate IDs, malformed UTF-8/UTF-16, blank text, controls, trailing bytes, and invalid counts fail before renderer access. The response is `3` followed by an Int32 count of translated render caches; zero can mean that chat is hidden or no matching messages are rendered. Display updates do not use the send commit handshake and never call `IGame.Send`.

Operation `3` restores display caches and requires account zero, empty target, and empty payload. It uses only an existing connection and does not attach a bridge just to clean up. Restore is authenticated but does not require a still-connected account, so disconnect/account changes cannot prevent cleanup. Check/send payloads, text validation and send commit semantics stay as before.

`StableNativeChat` resolves fields and methods lazily, so an unsupported renderer does not prevent ordinary sender use. It validates exact string/DateTime/float/vector types, render method signatures, list contracts and generic sprite add/remove methods. The original message object is shallow-cloned; only the clone receives the translation and null markup/cache. The native render factory builds a fresh renderer from that clone, keeping the sender/timestamp styling. The adapter swaps the actual message's **render cache only**, updates the game's sprite manager, disposes the old renderer, and adjusts line positions, scroll extent and reading anchor for height changes. Translated renders deliberately have no stale original-link hitboxes. Restoring rebuilds renderers from the original message and restores its link markup.

The native member inventory is `StableCompatibilityProfile.NativeChat`. These are reflected members, not additional raw-memory offsets. All accesses and renderer cleanup run on the game scheduler. Vector component fields use their ordinary names `X` and `Y`.

| Profile member | Exact name | Contract / use |
| --- | --- | --- |
| `SelectedRoom` | `#=znBcTsQhuDDlW` | Chat static field identifying the game-selected room |
| `RoomMessages` | `#=zZPApYNO_VE8T` | Room field; generic list implementing IList |
| `MessageBody` | `#=zo2TvRLU=` | Message string field; original body |
| `MessageSender` | `#=z$Skj1XQ=` | Message string field; original sender label |
| `MessageTime` | `#=zVoNQ1Jg=` | Message DateTime field |
| `MessageUser` | `#=zZPBGk$8=` | Message user field; ID resolved through Members.UserId |
| `MessageMarkup` | `#=zVd$ez8s5_Ka_` | Message original-link markup field |
| `MessageRender` | `#=zKkEaGpc$iohi` | Message renderer cache field; the reference replaced locally |
| `GetRender` | `#=zM9jH2gHVIIG0` | Message instance method () -> renderer cache type |
| `RenderSprites` | `#=zeXe9zp2TbwiT` | Renderer field; generic sprite collection |
| `RenderPosition` | `#=z5dJXJudb9M8u` | Renderer instance method () -> vector with float X/Y fields |
| `MoveRender` | `#=zopyyEGNWA8R4` | Renderer instance method (vector) -> void |
| `RenderHeight` | `#=zF7J0an4ScLrC` | Renderer instance method () -> float |
| `DisposeRender` | `#=ziqiaHyE=` | Renderer instance method () -> void |
| `Scroll` | `#=zokHNinD$C3CI` | Chat static field; scrolling container |
| `ScrollSprites` | `#=z3G9vcE574eaFv$hCpA==` | Scrolling container field; sprite manager |
| `ScrollExtent` | `#=zGYuE6bu9JLJ4` | Scrolling container field; extent vector |
| `ScrollPosition` | `#=zVWON5kIsyabS` | Scrolling container field; reading-position vector |
| `SetExtent` | `#=zTjwc4xiz0aNR` | Scrolling container instance method (vector) -> void |
| `SetScroll` | `#=zgV0iGqaGhq1U` | Scrolling container instance method (vector, float) -> void |
| `ViewHeight` | `#=z47Kz$J4RkGA2Pe5ApQ==` | Scrolling container instance method () -> float |
| `AddSprites` | `#=zmX6lT8Y=` | Sprite manager generic instance method (IEnumerable<T>) -> void |
| `RemoveSprites` | `#=zMXQUAGk=` | Sprite manager generic instance method (List<T>) -> void |

`NativeDisplayLease` schedules refreshes every 250 ms with at most one outstanding callback. Each accepted batch renews a three-second lease. Missing refreshes, disconnect/account/state rejection, explicit disable/pause, bridge stop, or host loss restore originals on the scheduler. A stopped scheduler delays restoration until it runs again. Each refresh revalidates identity, connection, menu/lobby state, target existence and the game-selected room, matches IDs against original messages, and skips own/system messages. Tab changes invalidate surviving translated caches in the previous room. The implementation does not hook network receive methods, rewrite original chat strings, alter the selected tab or send messages.

### Verifying native display

Manual checks for future builds: enable native mode with a cached/local provider; verify a new Russian or Arabic incoming line becomes the chosen target language; switch game tabs independently of Chatter; scroll history while translations finish; check long wrapped lines, duplicate messages, actions, links, own/system exclusions; then disable, pause, disconnect, change account, enter gameplay, close/kill Chatter and restart osu!. Verify originals return and app/overlay IDs/unread state remain stable. Actual sending is a separate manual check and must only use a deliberately chosen target and message.


## Runtime validation

The shared profile has an identifier and a recorded executable SHA-256. Settings → About copies version/hash/profile and distinguishes a matching fingerprint from an unverified executable. The reader checks x86 architecture before attaching, scans all signature hits, and rejects hits resolving to distinct storage operands. Repeated JIT sites resolving to the same operand are accepted. Chat roots are structurally verified, with a single valid root required. Account observations never reuse stale identity. Reflection validates scheduler return/callback types, string/bool/list contracts and required members. CompatibilityException carries typed reader errors; bridge startup distinguishes missing/type members (12), invalid operations (13), and unexpected failures (11).

Runtime diagnostics compare the executable fingerprint and read the installed build for information only; there is no in-app release-feed lookup or official-build allowlist. Runtime architecture, signature, graph, reflected-member and account checks remain local to the actual integration.

## Native outgoing command hook

`StableCompatibilityProfile.NativeCommands` additionally names the native input type and field used for guarded draft restoration:

| Profile member | Exact name | Use |
| --- | --- | --- |
| InputType | `#=zAYjsC2Sevl3$ROIGxg==` | Chat input type in the game assembly |
| InputField | `#=z$0OIVB8=` | Static input instance; its Text property is accessed with public/nonpublic instance reflection, only when the same account/room is selected and the editor is empty |

The command prefix patches the sender identified by `Members.Send`; it does not introduce a separate receive-hook address.

The bridge uses a release-specific simple assembly name (`Chatter.Bridge.Runtime_<version>`) and assembly version 2.0.0.0. The shared build metadata drives both its filename and embedded-resource extraction; changing only AssemblyVersion does not defeat CLR weak-name reuse. Lib.Harmony 2.4.3 is embedded with the app, extracted beside the hash-isolated bridge and verified against the bundled bytes. Operation 4 validates a fresh account/menu/target, accepts a bounded take/completed-id/success acknowledgement payload, and polls the local command queue. It returns response 3, a little-endian byte length, and a NativeCommandProtocol record (or no record). Operation 5 restores pending drafts when safe and removes the hook; it requires zero account, empty target/payload, authenticates normally and only uses an existing connection. Command failures return bounded local diagnostics. Existing send/check/display semantics are retained.

Only exact first tokens /t and /translate are intercepted. A recognized command is consumed before the original sender runs, including malformed commands. The language/body are validated locally; nested slash commands, controls, blank input and oversized text are rejected. No translation happens on the game's UI thread. The queue retains the original account, target, original command, ID and 45-second deadline. At most eight commands wait; failures restore the original input only for the same selected account/room and an empty editor. No overwrite of a newer draft is allowed.

An enabled hook has a three-second heartbeat lease. The app polls once a second while fresh reading, account connection and menus/lobby remain valid. Pausing, disabling commands/settings changes and closing increment a cancellation generation. Translation completion rechecks the captured identity and deadline; cancellation is checked again immediately before the existing nonce/deadline send commit. Commands use the normal guarded sender once, with no send retries. Normal chat/game commands continue through the original method. Session shutdown schedules unpatching on the game scheduler.

Offline checks patch an isolated no-inline fixture sender, never the game's live sender, to verify capture, ordinary-command pass-through, malformed alias consumption, draft restoration and unpatch. The separate --commands-live probe installs/removes the real hook without calling the sender. Actual translated sending remains an owner acceptance step with an explicitly chosen target and message.

## Display and transcript behavior

Native batches optionally append the original in brackets; original-only disables the display lease. Message bodies/IDs remain original. Automatic work covers configurable recent/new/history policies and prioritizes visible rows. Normal provider progress stays in a fixed status area instead of resizing the transcript. The modern transcript patches keyed rows and restores its reading anchor; it uses textContent and validated link ranges. Its private WebView2 document admits only its exact generated data URI/about:blank, blocks external navigation, and denies browser permissions. The fallback renderer remains available. No translation/chat content is logged to disk; setup-only diagnostics contain no chat input.


## Bridge loading

The CLR can reuse an already loaded weak-named assembly after a version-only update. The bridge therefore includes the release version in its simple assembly name. An isolated emitter/stub regression covers the same CLR host path: a changed assembly version under the old name still executes the old code, while a distinct name executes the new code.

Harmony owner IDs are unique per command-handler instance, so delayed cleanup can't remove a newer session's prefix. `--commands-live` installs/removes the hook without sending; `--inspect-commands-live <report>` records metadata and state without chat text.
