using System;
using System.Drawing;
using System.Reflection;

namespace Chatter.App {
    internal static class AppIcon {
        static readonly Icon shared = Load();

        public static Icon Create() => (Icon)shared.Clone();

        static Icon Load() {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Chatter.App.Resources.chatter.ico")) {
                if (stream == null) throw new InvalidOperationException("The application icon resource is missing.");
                return new Icon(stream);
            }
        }
    }
}
