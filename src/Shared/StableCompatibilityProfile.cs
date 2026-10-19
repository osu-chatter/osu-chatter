using System;

namespace Chatter.Shared {
    /// <summary>
    /// The single osu!stable layout currently used by the reader and in-process bridge.
    /// Source-linked into both assemblies: update game-specific values here together.
    /// This inherited layout has no recorded tested build; it is not a build allowlist.
    /// See docs/stable-compatibility.md before claiming support for a new client build.
    /// </summary>
    internal static class StableCompatibilityProfile {
        internal const string Id = "existing-stable-layout";
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

        internal static class Members {
            internal const string ChatType = "#=zOq1FT9SecrTxVa8ldCQtKMNEVVtMYwauiA==";
            internal const string GameType = "#=zLQ1lSsp1Sl649eje$Q==";
            internal const string BanchoType = "#=zZTtDI9i$TQmcWwn1v_pQEsI=";
            internal const string RoomType = "#=z9M3mPCP4CJ8T84$nrvqz2TqUKq64";
            internal const string Send = "#=zpQuniAY=";
            internal const string User = "#=zpJrV5O4=";
            internal const string UserId = "#=z5_k5yXY=";
            internal const string Mode = "#=zFX09SEA=";
            internal const string Connected = "#=z_n7Ve0k=";
            internal const string Ready = "#=z0C$NjaaaFPfo";
            internal const string Rooms = "#=zvdjfD6H60jaVvrMMyw==";
            internal const string RoomName = "#=z7OdQVus=";
            internal const string Scheduler = "#=zlLqslSk=";
            internal const string Schedule = "#=zuqLmyLg=";

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
