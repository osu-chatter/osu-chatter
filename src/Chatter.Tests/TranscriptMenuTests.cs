using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Chatter.App;

namespace Chatter.Tests {
    static class TranscriptMenuTests {
        const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static object Get(object view, string name) => view.GetType().GetField(name, Members).GetValue(view);
        static void Call(object view, string method, params object[] args) => view.GetType().GetMethod(method, Members).Invoke(view, args);
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Click(ContextMenuStrip menu, ToolStripItem item) {
            Check(menu.Visible && ReferenceEquals(item.Owner, menu), "menu item is not in the open menu");
            // PerformClick enters WinForms' item-click/auto-close path without sending desktop input.
            item.PerformClick();
            Application.DoEvents();
        }
        public static void TranslationTargets() {
            var first = new ChatMessage("first", "Friend", "hello", "12:00:00", false, false);
            var second = new ChatMessage("second", "Friend", "another message", "12:01:00", false, false);
            ContextMenuStrip? targetMenu = null, messageMenu = null;
            using (var view = new TranscriptView { Size = new Size(700, 420) }) {
                var handle = view.Handle;
                view.SetMessages(new[] { first, second }, new Dictionary<string, string>(), new Dictionary<string, string>(), true);
                ChatMessage? selectedMessage = null; string selectedTarget = ""; int requests = 0;
                view.TargetTranslationRequested += (message, target) => { selectedMessage = message; selectedTarget = target; requests++; };
                for (int path = 0; path < 2; path++) {
                    for (int i = 0; i < ChatRules.Languages.Length; i++) {
                        ChatMessage expected = i % 2 == 0 ? first : second;
                        if (path == 0) Call(view, "HandleModernAction", "translate-to", expected.Id, "");
                        else {
                            var block = ((IList)Get(view, "blocks"))[i % 2];
                            Rectangle bubble = (Rectangle)Get(block, "Bubble");
                            Call(view, "OnMouseDown", new MouseEventArgs(MouseButtons.Right, 1, bubble.Left + 8, bubble.Top - (int)Get(view, "scroll") + 8, 0));
                            messageMenu = (ContextMenuStrip)Get(view, "messageMenu");
                            ToolStripItem? translate = null;
                            foreach (ToolStripItem item in messageMenu.Items) if (item.Text == "Translate to…") translate = item;
                            Check(translate != null, "missing built-in translation action");
                            Click(messageMenu, translate!);
                        }
                        targetMenu = (ContextMenuStrip)Get(view, "translationTargetMenu");
                        Check(targetMenu.Visible, "language picker did not open");
                        Check(targetMenu.Items.Count == ChatRules.Languages.Length, "missing language choices");
                        Click(targetMenu, targetMenu.Items[i]);
                        Check(requests == path * ChatRules.Languages.Length + i + 1, "language selection did not dispatch exactly once");
                        Check(ReferenceEquals(selectedMessage, expected) && selectedTarget == ChatRules.Languages[i], "translation used a stale message or wrong language");
                        Check(!targetMenu.Visible, "language picker remained open after selection");
                    }
                }
                Call(view, "HandleModernAction", "translate-to", first.Id, "");
                targetMenu = (ContextMenuStrip)Get(view, "translationTargetMenu");
                targetMenu.Close(ToolStripDropDownCloseReason.Keyboard);
                Check(requests == ChatRules.Languages.Length * 2, "cancel dispatched a translation");
                Call(view, "HandleModernAction", "translate-to", second.Id, "");
            }
            Check(targetMenu != null && targetMenu.IsDisposed && messageMenu != null && messageMenu.IsDisposed, "transcript disposal leaked menus");
        }
    }
}
