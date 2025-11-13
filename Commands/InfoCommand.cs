using System;
using CommandSystem;
using LabPlayer = LabApi.Features.Wrappers.Player;

namespace JFDCustomNameInfo.Commands
{
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    [CommandHandler(typeof(ClientCommandHandler))]
    public class InfoCommand : ICommand
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
                    // Clear custom info
                    try { JFDCustomNameInfo.NameAndCInfo.SetCustomInfoForUser(labPlayer.UserId, string.Empty); } catch { }
                        try { labPlayer.CustomInfo = string.Empty; } catch { }
                        response = "Custom info reset.";
                        try { JFDCustomNameInfo.NameAndCInfo.MarkCommandHandled(labPlayer.UserId); } catch { }
                        return true;
                }

                var info = (arguments.Count > 0) ? string.Join(" ", arguments.Array, arguments.Offset, arguments.Count).Trim() : string.Empty;
                try { JFDCustomNameInfo.NameAndCInfo.SetCustomInfoForUser(labPlayer.UserId, info); } catch { }
                try { labPlayer.CustomInfo = info; } catch { }
                response = "Custom info set.";
                try { JFDCustomNameInfo.NameAndCInfo.MarkCommandHandled(labPlayer.UserId); } catch { }
                return true;
            }
            catch
            {
                response = "Error setting custom info.";
                return false;
            }
        }

        public string Command { get; } = "info";

        public string[] Aliases { get; } = new[] { "cinfo" };

        public string Description { get; } = "Set or reset your custom info. Usage: info <text> (or info to reset).";
    }
}
