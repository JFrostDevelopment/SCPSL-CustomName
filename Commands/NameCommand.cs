using System;
using CommandSystem;
using LabPlayer = LabApi.Features.Wrappers.Player;

namespace JFDCustomNameInfo.Commands
{
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    [CommandHandler(typeof(ClientCommandHandler))]
    public class NameCommand : ICommand
    {
        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            try
            {
                if (!LabPlayer.TryGet(sender, out var labPlayer) || labPlayer == null)
                {
                    response = "Only players can use this command.";
                    return false;
                }

                if (arguments.Count == 0)
                {
                    // Reset to original nickname if available
                    try
                    {
                        var orig = JFDCustomNameInfo.NameAndCInfo.TryGetOriginalNicknameFor(labPlayer);
                        if (!string.IsNullOrEmpty(orig))
                        {
                            labPlayer.DisplayName = orig;
                            response = $"Nickname reset to original: {orig}";
                            return true;
                        }
                    }
                    catch { }

                    labPlayer.DisplayName = string.Empty;
                    response = "Nickname reset.";
                    try { JFDCustomNameInfo.NameAndCInfo.MarkCommandHandled(labPlayer.UserId); } catch { }
                    return true;
                }

                var newName = (arguments.Count > 0) ? string.Join(" ", arguments.Array, arguments.Offset, arguments.Count).Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(newName) || newName.Length > 32)
                {
                    response = "Nickname must be 1-32 characters.";
                    return false;
                }

                labPlayer.DisplayName = newName;
                response = $"Nickname set to {newName}";
                try { JFDCustomNameInfo.NameAndCInfo.MarkCommandHandled(labPlayer.UserId); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                response = "Error setting nickname.";
                return false;
            }
        }

        public string Command { get; } = "n";

        public string[] Aliases { get; } = new[] { "name" };

        public string Description { get; } = "Set or reset your nickname. Usage: n <name> (or n to reset).";
    }
}
