# osu!stable compatibility: maintenance guide and build verification proposal

Status: the shared layout extraction and fresh account reader are implemented. Exact build selection, stronger signature/member validation, typed compatibility errors, and release monitoring remain proposed work. The repository records no tested osu!stable build identifier or executable fingerprint. The inherited layout must not be described as supporting the latest Stable release without a client check.

## Updating the integration

The game-specific values have one edit location: [`src/Shared/StableCompatibilityProfile.cs`](../src/Shared/StableCompatibilityProfile.cs), source-linked into both Core and Bridge. `Chat` contains the root signature and chat field offsets; `Account` contains account/state signatures, operand offsets, profile fields and login values; `Members` contains in-process type/member names and invocation flags. The allowed menu/lobby states live in the same profile. There is one inherited profile, with no historical offset collection or new claim of build support.

[`Clr32Layout.cs`](../src/Chatter.Core/Clr32Layout.cs) separately names x86 CLR string/list/array and primitive layout assumptions. Keep these separate from osu! field changes. `OsuChat` handles process/discovery/retries, `StableAccountReader` decodes fresh identity/state observations, `ChatGraph` decodes chat, and `StableGame` resolves and invokes game members. UI and translation code do not consume offsets. Existing public `ChatRules` login/state helpers forward to the profile for source compatibility.

When updating a layout, identify the changed fields/signatures/members on the actual client, edit the shared profile, and update independently specified fixtures in `Chatter.Tests` to represent that observed layout. Run `check.bat -NoPause`, then perform the actual-client checks below before claiming compatibility. Rebuild the entire solution so the reader and embedded bridge use the same values. Do not update CLR offsets or weaken validation simply to make a broken game layout pass.

The extraction preserves existing signature discovery and send protocol. The safety change is that failed account reads now produce a fresh unavailable identity, and an unreadable login produces unknown connection status; neither reuses prior send authority.

Validation on 2026-10-17: a clean Release solution build completed with zero errors (111 existing warnings), all 26 offline checks and the startup smoke test passed, and an isolated copy containing only `chatter.exe` and its config passed startup with embedded dependencies. Against the running osu!stable client, `Chatter.Tests.exe --live` read 3 tabs and 12 messages with a connected account, and `ChatService.Check` successfully attached the bridge and validated an open conversation without sending. Actual message transmission and the full per-build manual matrix below were not exercised; this is not a supported-build certification.

## Current paths

Reading: `MainForm.StartPoll` -> `ChatService.Read` -> `OsuChat.Read` -> `Prepare` -> `RefreshProfile` -> `ChatGraph.Read` -> `ChatSnapshot`.

`Prepare` finds the first process named `osu!.exe`, opens a read/query handle, scans executable memory for candidate chat roots, and accepts exactly one distinct root whose graph can be read. `RefreshProfile` separately scans for account, login, and game-state addresses and delegates account decoding to `StableAccountReader`. `Read` retries inconsistent graphs; after three failures it discards the root. Discovery retries are throttled to five seconds. A dead process clears the root, signature addresses, identity, and retry state before attachment.

Sending: `ChatService.Plan` captures account/PID and an open target -> translation -> `Send` rechecks identity and target -> `BridgeClient.Request` attaches the managed bridge if needed -> `Entry.Start` constructs `StableGame` -> `Session.Serve` schedules work -> `StableGame.Validate` -> commit -> a second `Validate` -> `StableGame.Send`.

The bridge invokes the game's own sender on its scheduler. The reader does not write chat structures. Sending does not change the selected tab. After acknowledgment, `ChatService` waits 150 ms and checks for a new matching outgoing echo; absence of an echo does not trigger a resend.

## Compatibility inventory

Offsets below are bytes, including the x86 CLR method-table word where applicable. They describe source assumptions, not a verified build specification.

### Discovery and account (`OsuChat.cs`, `StableAccountReader.cs`, `ProcessMemory.cs`)

| Item | Current assumption |
| --- | --- |
| Process identity | First case-insensitive `osu!.exe` match; no build, release stream, or architecture validation |
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

`ProcessMemory.Scan` uses the first match for each named pattern and stops when all names have matches. It does not count alternative matches. It scans committed readable regions (read-only, read/write, execute/read, execute/read/write), skips regions over 100 MiB, and skips non-image, non-executable regions over 1 MiB. Chunks advance by 64 KiB and include the longest pattern as overlap. Result addresses must be positive and below `uint.MaxValue`.

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
| Chat type | `#=zOq1FT9SecrTxVa8ldCQtKMNEVVtMYwauiA==` | Entry assembly type |
| Game type | `#=zLQ1lSsp1Sl649eje$Q==` | Entry assembly type |
| Bancho type | `#=zZTtDI9i$TQmcWwn1v_pQEsI=` | Entry assembly type |
| Room type | `#=z9M3mPCP4CJ8T84$nrvqz2TqUKq64` | Entry assembly type |
| Send method | `#=zpQuniAY=` | Chat static method `(string, string, bool) -> void`, nonnull method body; invoked with `(text, target, false)` |
| Current user | `#=zpJrV5O4=` | Game static field |
| User ID | `#=z5_k5yXY=` | Instance field on current-user field type; must be `int` |
| Mode | `#=zFX09SEA=` | Game static field; value converted to `int` |
| Connected | `#=z_n7Ve0k=` | Bancho static field; value cast to `bool` |
| Ready | `#=z0C$NjaaaFPfo` | Bancho static field; value cast to `bool` |
| Rooms | `#=zvdjfD6H60jaVvrMMyw==` | Chat static field; runtime value must implement `IList` |
| Room name | `#=z7OdQVus=` | Room instance field; value cast to `string` |
| Scheduler | `#=zlLqslSk=` | Game static field; runtime value must be nonnull |
| Schedule method | `#=zuqLmyLg=` | Exactly one matching instance method with two parameters, second `bool`; invoked with `(callback, true)` |
| Callback | First schedule parameter type | Must be a delegate with `Invoke() -> void` |

Required types/fields must exist; reflection errors prevent bridge startup. Only the send return/body, callback contract, and user-ID type are explicitly checked at construction. Other field contracts are checked implicitly by casts/operations when used. The schedule method's own return type is not checked, and missing `Invoke` is not explicitly handled before dereference. Do not document these as stronger validation than the code provides.

Every scheduled validation requires a valid target, positive requested account, matching current user ID, connected and ready flags, an allowed game state, and a current room list of at most 512 containing the exact target name. A malformed room/member access also fails the operation. Validation runs both before granting commit and immediately before send.

### Sending safeguards outside the layout

These stay in the existing service/transport/rule boundaries:

- `ChatService` checks PID/account equality across translation, target membership and snapshot PID, validates text, and serializes check/send operations with an interlocked gate.
- `SharedChatRules` limits text to 400 UTF-16 code units, rejects blank/control-containing text and a leading slash after leading whitespace; target rules reject blank/control-containing names, names over 160 units, `#userlog`, `#highlight`, and leading `/`. Those target conventions should be documented alongside the adapter if they change, while message validation remains shared policy.
- `BridgeClient` verifies the extracted bridge bytes against its bundled/sidecar source, resolves exports relative to actual modules, checks allocation/write/protection/thread results, and bounds startup (8 seconds, then up to 3 seconds connecting). It retains remote allocations if the startup thread may still be running.
- Startup options require four lines, a `chatter-` pipe name of at most 100 letters/digits/hyphens, a 32-byte hex key, and positive host PID. Host start time is parsed and used to detect host replacement. Startup exceptions currently collapse to exit code 11 (invalid options use 10).
- Pipe endpoints verify the peer PID; the server restricts its ACL to the current Windows user and checks host PID plus start time. The 32-byte random key is compared without an early exit. The key is cleared when the session ends.
- Protocol magic is `0x52424331`; operations are check `0` or send `1`. The header is 45 bytes (key at 4, operation at 36, account at 37, target length at 41). Strict UTF-8 and byte bounds (target 1–640, text 0–1600) precede rule validation; check requests require empty text.
- Scheduled work expires after 2 seconds and checks cancellation, host liveness, and session stop state. A send requires a matching eight-byte nonce plus an unexpired deadline no more than 100 ms ahead; the client grants 75 ms. The server waits 100 ms for commit, bounds initial reads to 2 seconds, response writes to 200 ms, and scheduled completion to 2300 ms. Client request/status I/O is bounded to 2300 ms and commit/result I/O to 200 ms. Pipe acceptance waits at most 60 seconds.
- Response bytes are ready `1`, sent `2`, check passed `3`, rejected `4`. Transport failure after commit reports uncertainty and does not automatically retry a message.

`Directory.Build.props` makes the app x86 and the projects target .NET Framework 4.8. `ClrStub.cs` depends on x86 calling conventions and CLR v4 hosting (`clr.dll`, `mscoree.dll`, `CLRCreateInstance`, `v4.0.30319`; hosting COM slots 3/5/9/11 and Release slot 2). `NativeMethods.cs` owns Windows access flags and APIs. These are platform/runtime assumptions to validate and keep separate from per-build osu! names and offsets. Bridge entry names, wire offsets/magic, and COM offsets are not game layout constants.