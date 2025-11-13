using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using Exiled.API.Features;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using CommandSystem;
using LabPlayer = LabApi.Features.Wrappers.Player;

namespace JFDCustomNameInfo
{
    public class NameAndCInfo : Plugin<Config>
    {
        public override string Author => "JFDev";
        public override string Name => "JFDCustomNameInfo";
        public override Version Version => new Version(1, 0, 0);

        private static readonly string SaveFile = Path.Combine(Paths.Configs, "JFDCustomNameInfo_custominfo.txt");
        private static readonly ConcurrentDictionary<string, string> CustomInfo = new ConcurrentDictionary<string, string>();
    // Tracks recently-handled user commands so the ServerEvents fallback won't double-handle them.
    private static readonly ConcurrentDictionary<string, DateTime> RecentlyHandled = new ConcurrentDictionary<string, DateTime>();

        public override void OnEnabled()
        {
            base.OnEnabled();
            LoadCustomInfo();
        }

        public override void OnDisabled()
        {
            base.OnDisabled();
        }

        private void OnCommandExecuting(CommandExecutingEventArgs ev)
        {
            try
            {
                // Normalize command name: accept both ".name" and "name"
                var raw = ev.CommandName?.ToLower() ?? string.Empty;
                var cmd = raw.StartsWith(".") ? raw.Substring(1) : raw;
                var argsSeg = ev.Arguments; // ArraySegment<string>
                var args = argsSeg.Count > 0 ? argsSeg.ToArray() : Array.Empty<string>();

                // Try to get a LabApi player wrapper from the command sender
                if (!LabPlayer.TryGet(ev.Sender, out var labPlayer) || labPlayer == null)
                    return; // not a player (could be console/admin)

                // If a recent ICommand run already handled a command for this user, skip to avoid duplicate effects
                try
                {
                    if (RecentlyHandled.TryRemove(labPlayer.UserId, out var ts))
                    {
                        if ((DateTime.UtcNow - ts).TotalSeconds < 5)
                        {
                            if (Config.Debug) Log.Debug($"JFDCustomNameInfo: Skipping ServerEvents handler because ICommand already handled command for {labPlayer.UserId}.");
                            return;
                        }
                    }
                }
                catch { }

                // Optional debug logging
                if (Config.Debug)
                    Log.Debug($"JFDCustomNameInfo: Received command '{raw}' from {labPlayer.UserId} (resolved '{cmd}'), args={string.Join(" ", args)}");

                if (cmd == "n" || cmd == "name")
                {
                    // If there are no args, reset to the player's original nickname (if available) or clear DisplayName
                    if (args.Length == 0)
                    {
                        try
                        {
                            // Try to get an original nickname property via reflection (safe). Many LabApi wrappers expose "Nickname".
                            var orig = TryGetOriginalNicknameFor(labPlayer);
                            if (!string.IsNullOrEmpty(orig))
                            {
                                labPlayer.DisplayName = orig;
                                labPlayer.SendConsoleMessage($"Nickname reset to original: {orig}", "yellow");
                            }
                            else
                            {
                                // Fallback: clear custom display name
                                labPlayer.DisplayName = string.Empty;
                                labPlayer.SendConsoleMessage("Nickname reset.", "yellow");
                            }
                        }
                        catch { labPlayer.SendConsoleMessage("Nickname reset.", "yellow"); }

                        TryMarkCommandFound(ev);
                        return;
                    }

                    var newName = string.Join(" ", args).Trim();
                    if (string.IsNullOrWhiteSpace(newName) || newName.Length > 32)
                    {
                        labPlayer.SendConsoleMessage("Nickname must be 1-32 characters.", "yellow");
                        return;
                    }

                    // LabApi wrapper exposes DisplayName setter; Nickname getter is read-only, so set DisplayName
                    labPlayer.DisplayName = newName;
                    labPlayer.SendConsoleMessage($"Nickname set to {newName}", "yellow");

                    // If the command system didn't find a command, try to mark the event as handled so the "Command Not Found" message
                    // does not confuse players. We attempt to set IsAllowed = true (safe) and, if available, mark CommandFound.
                    TryMarkCommandFound(ev);
                }
                else if (cmd == "info" || cmd == "cinfo")
                {
                    // If no args are supplied, reset/clear the custom info for this user
                    if (args.Length == 0)
                    {
                        CustomInfo.TryRemove(labPlayer.UserId, out _);
                        SaveCustomInfo();
                        try { labPlayer.CustomInfo = string.Empty; } catch { }
                        labPlayer.SendConsoleMessage("Custom info reset.", "yellow");
                        TryMarkCommandFound(ev);
                        return;
                    }

                    var info = string.Join(" ", args).Trim();
                    // Save to our per-user dictionary and persist
                    CustomInfo[labPlayer.UserId] = info;
                    SaveCustomInfo();
                    // Also set LabApi wrapper custom info so other systems can read it immediately
                    try { labPlayer.CustomInfo = info; } catch { }
                    labPlayer.SendConsoleMessage("Custom info set.", "yellow");

                    TryMarkCommandFound(ev);
                }
            }
            catch (Exception ex)
            {
                if (Config.Debug)
                    Log.Error($"JFDCustomNameInfo: Exception in OnCommandExecuting: {ex}");
                // swallow to avoid crashing the event pipeline
            }
        }

        // Attempt to mark the command event as "found/handled" using reflection on common property/field names.
        private static void TryMarkCommandFound(object ev)
        {
            if (ev == null) return;
            try
            {
                var t = ev.GetType();
                // First, try the known IsAllowed property (already used) but do it defensively
                try
                {
                    var isAllowedProp = t.GetProperty("IsAllowed");
                    if (isAllowedProp != null && isAllowedProp.CanWrite && isAllowedProp.PropertyType == typeof(bool))
                        isAllowedProp.SetValue(ev, true);
                }
                catch { }

                // Try common boolean properties that different runtimes might expose
                var candidates = new[] { "CommandFound", "Found", "IsFound", "Handled", "IsHandled", "WasHandled", "FoundCommand" };
                foreach (var name in candidates)
                {
                    try
                    {
                        var p = t.GetProperty(name);
                        if (p != null && p.CanWrite && p.PropertyType == typeof(bool))
                        {
                            p.SetValue(ev, true);
                            return;
                        }
                        var f = t.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                        if (f != null && f.FieldType == typeof(bool))
                        {
                            f.SetValue(ev, true);
                            return;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // Try to get the underlying/original nickname from the LabApi player wrapper using reflection
        public static string TryGetOriginalNicknameFor(object labPlayer)
        {
            if (labPlayer == null) return null;
            try
            {
                var t = labPlayer.GetType();
                // Common property names to try
                var names = new[] { "Nickname", "Nick", "OriginalNickname", "DefaultNickname" };
                foreach (var n in names)
                {
                    try
                    {
                        var p = t.GetProperty(n);
                        if (p != null && p.CanRead && p.PropertyType == typeof(string))
                        {
                            var v = p.GetValue(labPlayer) as string;
                            if (!string.IsNullOrEmpty(v)) return v;
                        }
                    }
                    catch { }
                }

                // Fallback: try to read 'UserId' or 'Name' if present
                try { var p = t.GetProperty("UserId"); if (p != null) return p.GetValue(labPlayer)?.ToString(); } catch { }
                try { var p = t.GetProperty("Name"); if (p != null) return p.GetValue(labPlayer) as string; } catch { }
            }
            catch { }
            return null;
        }

        private static void LoadCustomInfo()
        {
            CustomInfo.Clear();
            if (!File.Exists(SaveFile)) return;
            foreach (var line in File.ReadAllLines(SaveFile))
            {
                var parts = line.Split('\t');
                if (parts.Length == 2)
                {
                    try { CustomInfo[parts[0]] = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])); } catch { }
                }
            }
        }

        private static void SaveCustomInfo()
        {
            try
            {
                File.WriteAllLines(SaveFile, CustomInfo.Select(kv => kv.Key + "\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(kv.Value))));
            }
            catch { }
        }

        // Public helper for commands to set or clear custom info for a user and persist it
        public static void SetCustomInfoForUser(string userId, string info)
        {
            if (string.IsNullOrEmpty(userId)) return;
            if (string.IsNullOrEmpty(info))
            {
                CustomInfo.TryRemove(userId, out _);
            }
            else
            {
                CustomInfo[userId] = info;
            }

            SaveCustomInfo();
        }

        // Mark that a command for this user was handled via ICommand so the ServerEvents handler can skip duplicate work.
        public static void MarkCommandHandled(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return;
            try { RecentlyHandled[userId] = DateTime.UtcNow; } catch { }
        }

    }
}
