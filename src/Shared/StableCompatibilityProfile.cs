using System;

namespace Chatter.Shared {
    /// <summary>
    /// The single osu!stable layout currently used by the reader and in-process bridge.
    /// Source-linked into both assemblies: update game-specific values here together.
    /// The local client fingerprint and exercised paths are recorded in the guide.
    /// This profile is not a build allowlist.
    /// See docs/stable-compatibility.md before claiming support for a new client build.
    /// </summary>
    internal static class StableCompatibilityProfile {
        internal const string Id = "stable-local-2026-11-21";
        internal const string TestedSha256 = "512B7F90B4D8A43E7895A029B36B4D18B34B35C0F66B4C463F8C2A9DA37F37E6";
        internal const string ProcessName = "osu!.exe";

        internal static class Chat {
            internal const string RootSignature = "8B 0D ?? ?? ?? ?? 8D 57 04 E8 ?? ?? ?? ?? B8 ?? ?? ?? ?? 89 47 0C 8B D7 ?? ?? ??";
            internal const int RootOperandOffset = 2;

            internal const uint AllRooms = 0;
            internal const uint SelectedRoomBeforeRoot = 20;
            internal const uint TabManager = 4;
            internal const uint ManagerTabs = 0x1C;
            internal const uint TabRoom = 12;
            internal const uint RoomName = 4;
            internal const uint RoomMessages = 16;
            internal const uint MessageBody = 4;
            internal const uint MessageSender = 8;
            internal const uint MessageMarkup = 16;
            internal const uint MarkupText = 12;
            internal const uint MessageTime = 36;
        }

        internal static class Account {
            internal const string StateSignature = "48 83 F8 04 73 1E";
            internal const int StateOperandOffset = -0x4;
            internal const string ProfileSignature = "FF 15 ?? ?? ?? ?? A1 ?? ?? ?? ?? 8B 48 54 33 D2";
            internal const int ProfileOperandOffset = 0x7;
            internal const string LoginSignature = "B8 0B 00 00 8B 35";
            internal const int LoginOperandOffset = -0xB;

            internal const uint Name = 0x30;
            internal const uint Id = 0x70;
            internal const int ConnectedLogin = 65793;
            internal const int GuestLogin = 256;
            internal const string GuestName = "guest";

            internal static bool LoggedIn(int id, string name, int login) {
                return id > 0 && login != GuestLogin && !name.Trim().Equals(GuestName, StringComparison.OrdinalIgnoreCase);
            }
        }

        // REBORN/tosu configuration dictionary, used only by explicit diagnostics.
        internal static class ClientVersion {
            internal const string ConfigurationSignature = "8D 45 EC 50 8B 0D ?? ?? ?? ?? 8B D7 39 09 E8 ?? ?? ?? ?? 85 C0 74 ?? 8B 4D EC";
            internal const int OperandOffset = 6;
            internal const uint Entries = 8;
            internal const uint Count = 0x1C;
            internal const uint EntrySize = 0x10;
            internal const uint EntryKey = 0;
            internal const uint EntryBindable = 4;
            internal const uint BindableValue = 4;
            internal const string Key = "LastVersion";
        }

        // Observed on the local executable recorded in docs/stable-compatibility.md.
        // These are renderer/cache members, never the network receive/send path.
        internal static class NativeCommands {
            internal const string InputType = "#=zAYjsC2Sevl3$ROIGxg==";
            internal const string InputField = "#=z$0OIVB8=";
        }

        internal static class NativeChat {
            internal const string SelectedRoom = "#=znBcTsQhuDDlW";
            internal const string RoomMessages = "#=zZPApYNO_VE8T";
            internal const string MessageBody = "#=zo2TvRLU=";
            internal const string MessageSender = "#=z$Skj1XQ=";
            internal const string MessageTime = "#=zVoNQ1Jg=";
            internal const string MessageUser = "#=zZPBGk$8=";
            internal const string MessageMarkup = "#=zVd$ez8s5_Ka_";
            internal const string MessageRender = "#=zKkEaGpc$iohi";
            internal const string GetRender = "#=zM9jH2gHVIIG0";
            internal const string RenderSprites = "#=zeXe9zp2TbwiT";
            internal const string RenderPosition = "#=z5dJXJudb9M8u";
            internal const string MoveRender = "#=zopyyEGNWA8R4";
            internal const string RenderHeight = "#=zF7J0an4ScLrC";
            internal const string DisposeRender = "#=ziqiaHyE=";
            internal const string Scroll = "#=zokHNinD$C3CI";
            internal const string ScrollSprites = "#=z3G9vcE574eaFv$hCpA==";
            internal const string ScrollExtent = "#=zGYuE6bu9JLJ4";
            internal const string ScrollPosition = "#=zVWON5kIsyabS";
            internal const string SetExtent = "#=zTjwc4xiz0aNR";
            internal const string SetScroll = "#=zgV0iGqaGhq1U";
            internal const string ViewHeight = "#=z47Kz$J4RkGA2Pe5ApQ==";
            internal const string AddSprites = "#=zmX6lT8Y=";
            internal const string RemoveSprites = "#=zMXQUAGk=";
        }

        internal static class Members {
            internal const string ChatType = "#=zfZ67dsuuO$fV485iSEUJubF$6dHYUCshjA==";
            internal const string GameType = "#=zUp$mEEXw9tnX4wTw9g==";
            internal const string BanchoType = "#=zjlzdmSZWCm7Gt_hCRy1j458=";
            internal const string RoomType = "#=zxm_sYRQOWDUquMAvmG8d3r1mUahY";
            internal const string Send = "#=zk23iJUA=";
            internal const string User = "#=zZPBGk$8=";
            internal const string UserId = "#=zOE3Zzjk=";
            internal const string Mode = "#=zPlM2ZxE=";
            internal const string Connected = "#=z2yQx9JE=";
            internal const string Ready = "#=zpKhmMXy_rJS1";
            internal const string Rooms = "#=znhMY61j4uPK$oUhrPw==";
            internal const string RoomName = "#=z$Skj1XQ=";
            internal const string Scheduler = "#=zxC1wkg8=";
            internal const string Schedule = "#=zmX6lT8Y=";

            // Arguments to the game's existing sender and scheduler, respectively.
            internal const bool SendFlag = false;
            internal const bool ScheduleFlag = true;
        }

        static readonly int[] MenuStates = { 0, 5, 7, 11, 12, 13, 14, 15, 17, 18 };

        internal static bool MenuOrLobby(int state) {
            for (int i = 0; i < MenuStates.Length; i++) if (MenuStates[i] == state) return true;
            return false;
        }
    }
}
