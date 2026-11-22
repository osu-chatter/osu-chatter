using System;
using System.Threading.Tasks;
using Chatter;
namespace Chatter.App {
    public sealed partial class MainForm {
        int commandGeneration;
        bool commandPolling, commandTranslating, commandsActive, commandsReady;
        DateTime commandPollAt;
        string completedCommand = "", commandError = "";
        bool completedSuccessfully = true;
        void UpdateNativeCommands() {
            if (DemoMode || commandPolling) return;
            var channel = GameSelectedChannel(); var observation = snapshot;
            bool enabled = preferences.Experience.NativeCommands && reading && readerError.Length == 0 && observation?.Connected == true && observation.MenuOrLobby && channel != null;
            if (!enabled) commandsReady = false;
            if (!enabled && commandsActive) System.Threading.Interlocked.Increment(ref commandGeneration);
            if (!enabled && !commandsActive) return;
            if (enabled && DateTime.UtcNow < commandPollAt) return;
            commandPolling = true; commandsActive = enabled; commandPollAt = DateTime.UtcNow.AddSeconds(1);
            string acknowledgement = completedCommand; bool succeeded = completedSuccessfully; bool take = !commandTranslating;
            Task.Run(() => {
                GameTranslationCommand? item = null; string error = "";
                try { if (enabled) item = service.PollCommands(observation!, channel!.Name, take, acknowledgement, succeeded); else service.StopCommands(); }
                catch (Exception ex) { error = ex.Message; }
                Post(() => {
                    commandPolling = false;
                    commandsReady = enabled && error.Length == 0;
                    if (error.Length == 0 && commandError.Length > 0) { if (actionError == "In-game commands: " + commandError) actionError = ""; commandError = ""; }
                    if (acknowledgement == completedCommand && error.Length == 0) completedCommand = "";
                    if (error.Length > 0 && commandError != error) { commandError = error; actionError = "In-game commands: " + error; Render(); }
                    if (item != null) {
                        if (preferences.Experience.NativeCommands && reading && readerError.Length == 0) ProcessNativeCommand(item);
                        else { completedCommand = item.Id; completedSuccessfully = false; }
                    }
                    if (!preferences.Experience.NativeCommands || !reading) UpdateNativeCommands();
                    UpdateTranslationStatus();
                });
            });
        }
        void ProcessNativeCommand(GameTranslationCommand command) {
            int generation = System.Threading.Volatile.Read(ref commandGeneration);
            commandTranslating = true;
            notice = "Translating your command for " + command.Target + "…";
            var request = new TranslationRequest { Text = command.Text, Source = "auto", Target = command.Language };
            preferences.ApplyProvider(request, false);
            if (snapshot != null) foreach (var channel in snapshot.Channels) if (channel.Name == command.Target) request.Context = TranslationContext.Recent(channel, preferences.Experience.ContextMessages);
            Task.Run(() => {
                string error = "", translated = "";
                try { translated = service.SendCommand(command, request, () => System.Threading.Volatile.Read(ref commandGeneration) == generation).Translated; }
                catch (Exception ex) { error = ex.Message; }
                Post(() => {
                    commandTranslating = false; completedCommand = command.Id; completedSuccessfully = error.Length == 0;
                    if (error.Length > 0) { actionError = "Command for " + command.Target + ": " + error; notice = ""; }
                    else { actionError = ""; notice = "Sent translated command to " + command.Target + ": " + translated; }
                    Render();
                });
            });
        }
    }
}
