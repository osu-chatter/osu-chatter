using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Chatter.App {
    sealed class WordTextBox : TextBox {
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
            if (keyData != (Keys.Control | Keys.Back)) return base.ProcessCmdKey(ref msg, keyData);
            // Consume the shortcut before the native edit control inserts a DEL character.
            if (ReadOnly || !ShortcutsEnabled) return true;

            if (SelectionLength == 0) {
                string text = Text;
                int end = SelectionStart, start = end;
                while (start > 0 && char.IsWhiteSpace(text[start - 1])) start--;
                while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
                if (start == end) return true;
                Select(start, end - start);
            }

            // Keep the deletion in the native undo buffer.
            SendMessage(Handle, 0x00C2 /* EM_REPLACESEL */, (IntPtr)1, "");
            return true;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, string text);
    }
}
