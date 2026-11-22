using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Chatter.Shared;
using N = Chatter.Shared.StableCompatibilityProfile.NativeChat;
using M = Chatter.Shared.StableCompatibilityProfile.Members;

namespace Chatter.Bridge {
    internal interface INativeChatGame {
        int Display(int account, string target, IDictionary<string, string> translations);
        void RestoreDisplay();
    }

    // All access, including cleanup, runs on the game's scheduler. The original
    // body, sender, timestamp and markup are never assigned, even temporarily.
    internal sealed class StableNativeChat {
        const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        readonly FieldInfo selected, roomName, messages, body, sender, time, messageUser, userId, markup, render, sprites;
        readonly FieldInfo scroll, manager, extent, viewport, x, y;
        readonly MethodInfo getRender, position, move, height, dispose, add, remove, setExtent, setScroll, viewHeight;
        readonly MethodInfo clone = typeof(object).GetMethod("MemberwiseClone", Instance);
        readonly Dictionary<object, Applied> applied = new Dictionary<object, Applied>();
        object? previousRoom;

        sealed class Applied { internal object Render = null!; internal string Text = ""; }

        internal StableNativeChat(Assembly assembly) {
            Type chat = assembly.GetType(M.ChatType, true), room = assembly.GetType(M.RoomType, true);
            selected = Field(chat, N.SelectedRoom, Static);
            roomName = Field(room, M.RoomName, Instance, typeof(string));
            messages = Field(room, N.RoomMessages, Instance);
            if (!messages.FieldType.IsGenericType || !typeof(IList).IsAssignableFrom(messages.FieldType)) throw Unsupported();
            Type message = messages.FieldType.GetGenericArguments().Single();
            body = Field(message, N.MessageBody, Instance, typeof(string));
            sender = Field(message, N.MessageSender, Instance, typeof(string));
            time = Field(message, N.MessageTime, Instance, typeof(DateTime));
            messageUser = Field(message, N.MessageUser, Instance);
            userId = Field(messageUser.FieldType, M.UserId, Instance, typeof(int));
            markup = Field(message, N.MessageMarkup, Instance);
            render = Field(message, N.MessageRender, Instance);
            getRender = Method(message, N.GetRender, render.FieldType);
            Type drawing = render.FieldType;
            sprites = Field(drawing, N.RenderSprites, Instance);
            position = Method(drawing, N.RenderPosition, null);
            Type vector = position.ReturnType;
            x = Field(vector, "X", Instance, typeof(float)); y = Field(vector, "Y", Instance, typeof(float));
            move = Method(drawing, N.MoveRender, typeof(void), vector);
            height = Method(drawing, N.RenderHeight, typeof(float));
            dispose = Method(drawing, N.DisposeRender, typeof(void));
            scroll = Field(chat, N.Scroll, Static);
            Type scrolling = scroll.FieldType;
            manager = Field(scrolling, N.ScrollSprites, Instance);
            extent = Field(scrolling, N.ScrollExtent, Instance, vector);
            viewport = Field(scrolling, N.ScrollPosition, Instance, vector);
            setExtent = Method(scrolling, N.SetExtent, typeof(void), vector);
            setScroll = Method(scrolling, N.SetScroll, typeof(void), vector, typeof(float));
            viewHeight = Method(scrolling, N.ViewHeight, typeof(float));
            Type sprite = sprites.FieldType.GetGenericArguments().Single();
            add = Generic(manager.FieldType, N.AddSprites, typeof(System.Collections.Generic.IEnumerable<>), sprite);
            remove = Generic(manager.FieldType, N.RemoveSprites, typeof(List<>), sprite);
        }

        static InvalidOperationException Unsupported() => new InvalidOperationException("This osu!stable build does not support in-game translation");
        static FieldInfo Field(Type type, string name, BindingFlags flags, Type? expected = null) {
            FieldInfo field = type.GetField(name, flags);
            if (field == null || (expected != null && field.FieldType != expected)) throw Unsupported();
            return field;
        }
        static MethodInfo Method(Type type, string name, Type? result, params Type[] args) {
            MethodInfo method = type.GetMethod(name, Instance, null, args, null);
            if (method == null || (result != null && method.ReturnType != result)) throw Unsupported();
            return method;
        }
        static MethodInfo Generic(Type type, string name, Type parameter, Type argument) {
            MethodInfo method = type.GetMethods(Instance).SingleOrDefault(m => m.Name == name && m.IsGenericMethodDefinition
                && m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType.IsGenericType && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == parameter);
            if (method == null || method.ReturnType != typeof(void)) throw Unsupported();
            return method.MakeGenericMethod(argument);
        }

        internal int Sync(int account, string target, IDictionary<string, string> translations) {
            object room = selected.GetValue(null);
            // A tab switch disposes that room's visible renderers in osu! itself.
            // Invalidate any surviving translated caches before leaving the room.
            if (!ReferenceEquals(previousRoom, room)) { RestoreCaches(); previousRoom = room; }
            if (room == null || (string)roomName.GetValue(room) != target) { Restore(); return 0; }
            IList? list = messages.GetValue(room) as IList;
            if (list == null || list.Count > 10000) throw Unsupported();
            var desired = new Dictionary<object, string>();
            var occurrences = new Dictionary<ulong, int>();
            for (int i = Math.Max(0, list.Count - NativeChatProtocol.MaxMessages); i < list.Count; i++) {
                object item = list[i];
                string text = (string)body.GetValue(item), label = (string)sender.GetValue(item);
                DateTime stamp = (DateTime)time.GetValue(item);
                if (text == null || label == null) throw Unsupported();
                bool system = text.Length == 0 || label.Length == 0;
                string display = system ? label : ChatMessageIdentity.DisplaySender(label, stamp.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                ulong hash = ChatMessageIdentity.Hash((ulong)stamp.Ticks, target, text.Length == 0 ? "" : display, text.Length == 0 ? label : text);
                occurrences.TryGetValue(hash, out int occurrence); occurrences[hash] = occurrence + 1;
                string id = hash.ToString("x") + "-" + occurrence;
                object player = messageUser.GetValue(item);
                bool own = player != null && (int)userId.GetValue(player) == account;
                if (!system && !own && translations.TryGetValue(id, out string translated) && translated != text) desired[item] = translated;
            }
            Update(list, desired);
            return applied.Count;
        }

        internal void Restore() {
            object room = selected.GetValue(null);
            if (ReferenceEquals(room, previousRoom) && room != null && messages.GetValue(room) is IList list) Update(list, new Dictionary<object, string>());
            RestoreCaches();
            previousRoom = null;
        }

        void RestoreCaches() {
            foreach (var item in applied) {
                if (!ReferenceEquals(render.GetValue(item.Key), item.Value.Render)) continue;
                // Detached room caches are rebuilt from original message fields next time.
                dispose.Invoke(item.Value.Render, null);
                render.SetValue(item.Key, null);
            }
            applied.Clear();
        }

        void Update(IList list, Dictionary<object, string> desired) {
            object scrolling = scroll.GetValue(null);
            if (scrolling == null) return;
            object spriteManager = manager.GetValue(scrolling);
            object oldExtent = extent.GetValue(scrolling), oldView = viewport.GetValue(scrolling);
            float total = Y(oldExtent), view = Y(oldView), viewSize = (float)viewHeight.Invoke(scrolling, null);
            bool bottom = view + viewSize >= total - 1;
            float shift = 0, anchorShift = 0;
            bool anchored = false;
            var retained = new HashSet<object>();
            for (int i = 0; i < list.Count; i++) {
                object item = list[i], current = render.GetValue(item);
                desired.TryGetValue(item, out string text);
                applied.TryGetValue(item, out Applied prior);
                if (current == null) { applied.Remove(item); continue; } // Lazy creation stays with osu!.
                object at = position.Invoke(current, null);
                float before = (float)height.Invoke(current, null), delta = 0;
                bool ours = prior != null && ReferenceEquals(current, prior.Render);
                if ((text != null && (!ours || prior!.Text != text)) || (text == null && ours)) {
                    object copy = clone.Invoke(item, null);
                    render.SetValue(copy, null);
                    if (text != null) { body.SetValue(copy, text); markup.SetValue(copy, null); }
                    object replacement = getRender.Invoke(copy, null);
                    try {
                        move.Invoke(replacement, new[] { at });
                        add.Invoke(spriteManager, new[] { sprites.GetValue(replacement) });
                        remove.Invoke(spriteManager, new[] { sprites.GetValue(current) });
                        render.SetValue(item, replacement);
                    } catch { dispose.Invoke(replacement, null); throw; }
                    dispose.Invoke(current, null);
                    current = replacement;
                    delta = (float)height.Invoke(current, null) - before;
                    if (text == null) applied.Remove(item);
                    else applied[item] = new Applied { Render = current, Text = text };
                }
                if (!anchored && Y(at) + before > view) { anchorShift = shift; anchored = true; }
                if (shift != 0) move.Invoke(current, new[] { Vector(at, Y(at) + shift) });
                shift += delta;
                retained.Add(item);
            }
            foreach (object expired in applied.Keys.Where(key => !retained.Contains(key)).ToArray()) applied.Remove(expired);
            if (shift != 0) {
                setExtent.Invoke(scrolling, new[] { Vector(oldExtent, Math.Max(0, total + shift)) });
                float next = bottom ? Math.Max(0, total + shift - viewSize) : Math.Max(0, view + anchorShift);
                setScroll.Invoke(scrolling, new object[] { Vector(oldView, next), 0f });
            }
        }

        float Y(object vector) => (float)y.GetValue(vector);
        object Vector(object original, float newY) {
            object value = Activator.CreateInstance(original.GetType());
            x.SetValue(value, x.GetValue(original)); y.SetValue(value, newY);
            return value;
        }
    }
}
