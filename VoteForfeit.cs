using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Unity.Collections;

namespace VoteForfeit;


public class VoteForfeit
{
    private const int minGoalDeficit = 3;
    private const uint timeoutSeconds = 60;

    private const string messagePrefix = "<color=orange><b>VoteForfeit</b></color>";
    private const string helpMessage = $"{messagePrefix} commands:\n"
        + "* <b>/forfeit</b> (/ff) - Vote to forfeit";


    private static Dictionary<FixedString32Bytes, DateTime> votes = [];

    [HarmonyPatch(typeof(UIChatController), nameof(UIChatController.Event_Server_OnChatCommand))]
    public static class UIChatControllerEventServerOnChatCommandPatch
    {
        [HarmonyPrefix]
        public static void Event_Server_OnChatCommand_Patch(
            Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Object> message)
        {
            if (message == null) return;
            if (!message.ContainsKey("command")) return;
            if (!message.ContainsKey("clientId")) return;

            PlayerManager playerManager = NetworkBehaviourSingleton<PlayerManager>.Instance;
            GameManager gameManager = NetworkBehaviourSingleton<GameManager>.Instance;
            UIChat uiChat = NetworkBehaviourSingleton<UIChat>.Instance;

            if (playerManager == null) return;
            if (gameManager == null) return;
            if (uiChat == null) return;

            string command = message["command"].ToString().ToLower();
            ulong clientId = (ulong)Il2CppSystem.Convert.ToInt64(message["clientId"]);

            if (command.Equals("/help")) { uiChat.Server_SendClientSystemChatMessage(helpMessage, clientId); return; }
            if (!(command.Equals("/forfeit") || command.Equals("/ff"))) return;

            DateTime now = DateTime.Now;
            GameState gameState = gameManager.GameState.Value;

            Player player = playerManager.GetPlayerByClientId(clientId);
            FixedString32Bytes steamId = player.SteamId.Value;
            PlayerTeam team = player.Team.Value;
            Il2CppSystem.Collections.Generic.List<Player> teammates = playerManager.GetPlayersByTeam(team);
            int needed = teammates.Count - 1;

            int goalDeficit;
            switch (team)
            {
                case PlayerTeam.Red:
                    goalDeficit = gameState.BlueScore - gameState.RedScore;
                    break;
                case PlayerTeam.Blue:
                    goalDeficit = gameState.RedScore - gameState.BlueScore;
                    break;
                default:
                    uiChat.Server_SendClientSystemChatMessage($"{messagePrefix} You must be in a team to forfeit.", clientId);
                    return;
            }

            if (goalDeficit < minGoalDeficit)
            {
                uiChat.Server_SendClientSystemChatMessage(
                    $"{messagePrefix} You must be losing by at least {minGoalDeficit} goals to forfeit.",
                    clientId
                );
                return;
            }

            // Requirements passed, add vote.
            votes = votes
                .Where(pair => now.Subtract(pair.Value).Seconds < timeoutSeconds)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            bool alreadyVoted = votes.ContainsKey(steamId);
            votes[steamId] = now;
            if (votes.Count >= needed)
            {
                uiChat.Server_SendSystemChatMessage($"{messagePrefix} {team} has forfeited the match.");
                votes.Clear();
                gameManager.Server_GameOver();
            }
            else if (!alreadyVoted)
            {
                uiChat.Server_SendSystemChatMessage($"{messagePrefix} Vote <b>forfeit</b> in progress ({votes.Count}/{needed}).");
            }
            else
            {
                uiChat.Server_SendClientSystemChatMessage($"{messagePrefix} You already recently voted to forfeit.", clientId);
            }
        }
    }
}
