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
    private const string redTeamText = $"<color=red><b>RED</b></color>";
    private const string blueTeamText = $"<color=blue><b>BLUE</b></color>";


    private static Dictionary<FixedString32Bytes, DateTime> votes = [];

    [HarmonyPatch(typeof(UIChatController), nameof(UIChatController.Event_Server_OnChatCommand))]
    public static class UIChatControllerEventServerOnChatCommandPatch
    {
        [HarmonyPrefix]
        public static void Event_Server_OnChatCommand(
            UIChatController __instance,
            Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Object> message)
        {
            if (__instance == null) return;
            if (message == null) return;
            if (!message.ContainsKey("command")) return;
            if (!message.ContainsKey("clientId")) return;

            PlayerManager playerManager = NetworkBehaviourSingleton<PlayerManager>.Instance;
            GameManager gameManager = NetworkBehaviourSingleton<GameManager>.Instance;
            UIChat uiChat = __instance.uiChat;

            if (playerManager == null) return;
            if (gameManager == null) return;
            if (uiChat == null) return;

            string command = message["command"].ToString().ToLower();
            ulong clientId = (ulong)Il2CppSystem.Convert.ToInt64(message["clientId"]);

            if (command.Equals("/help")) { uiChat.Server_SendSystemChatMessage(helpMessage, clientId); return; }
            if (!(command.Equals("/forfeit") || command.Equals("/ff"))) return;

            DateTime now = DateTime.Now;
            GameState gameState = gameManager.GameState.Value;

            Player player = playerManager.GetPlayerByClientId(clientId);
            FixedString32Bytes steamId = player.SteamId.Value;
            PlayerTeam team = player.Team.Value;
            Il2CppSystem.Collections.Generic.List<Player> teammates = playerManager.GetPlayersByTeam(team);
            int needed = teammates.Count - 1;

            // Check for valid game phase.
            if (gameState.Phase is GamePhase.None or GamePhase.Warmup or GamePhase.GameOver)
            {
                Plugin.Log.LogDebug($"{clientId} tried to forfeit, but the game phase was {gameState.Phase}.");
                uiChat.Server_SendSystemChatMessage(
                    $"{messagePrefix} You cannot vote when there is no match.",
                    clientId
                );
                votes.Clear(); // just in case
                return;
            }

            // Check if on a team.
            int goalDeficit;
            string teamText;
            switch (team)
            {
                case PlayerTeam.Red:
                    goalDeficit = gameState.BlueScore - gameState.RedScore;
                    teamText = redTeamText;
                    break;
                case PlayerTeam.Blue:
                    goalDeficit = gameState.RedScore - gameState.BlueScore;
                    teamText = blueTeamText;
                    break;
                default:
                    Plugin.Log.LogDebug($"{clientId} tried to forfeit, but they were not on a team.");
                    uiChat.Server_SendSystemChatMessage($"{messagePrefix} You must be in a team to forfeit.", clientId);
                    return;
            }

            // Check goal deficit.
            if (goalDeficit < minGoalDeficit)
            {
                Plugin.Log.LogDebug($"{clientId} tried to forfeit, but the goal deficit was not large enough: {goalDeficit}<{minGoalDeficit}.");
                uiChat.Server_SendSystemChatMessage(
                    $"{messagePrefix} You must be losing by at least {minGoalDeficit} goals to forfeit.",
                    clientId
                );
                return;
            }

            // Requirements passed, add vote.
            votes = votes
                .Where(pair =>
                {
                    bool recent = now.Subtract(pair.Value).Seconds < timeoutSeconds;
                    Player player = playerManager.GetPlayerBySteamId(pair.Key);
                    bool onTheTeam = player != null && player.Team.Value == team;
                    return recent && onTheTeam;
                })
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            bool alreadyVoted = votes.ContainsKey(steamId);
            votes[steamId] = now;

            if (votes.Count >= needed)
            {
                Plugin.Log.LogDebug($"Forfeit vote passed.");
                uiChat.Server_SendSystemChatMessage($"{messagePrefix} {teamText} has forfeited the match.");
                votes.Clear();
                gameManager.Server_GameOver();
            }
            else if (!alreadyVoted)
            {
                Plugin.Log.LogDebug($"Forfeit vote in progress [{votes.Count}/{needed}].");
                uiChat.Server_SendSystemChatMessage($"{messagePrefix} Vote <b>forfeit</b> in progress ({votes.Count}/{needed}).");
            }
            else
            {
                Plugin.Log.LogDebug($"{clientId} tried to forfeit, but they already voted recently.");
                uiChat.Server_SendSystemChatMessage($"{messagePrefix} You already recently voted to forfeit.", clientId);
            }
        }
    }

    [HarmonyPatch(typeof(UIChatController), nameof(UIChatController.Event_OnGoalScored))]
    public static class UIChatControllerEventOnGoalScoredPatch
    {
        [HarmonyPrefix]
        public static void Event_OnGoalScored(
            UIChatController __instance,
            Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Object> message)
        {
            if (__instance == null) return;

            GameManager gameManager = NetworkBehaviourSingleton<GameManager>.Instance;
            UIChat uiChat = __instance.uiChat;

            if (gameManager == null) return;
            if (uiChat == null) return;

            GameState gameState = gameManager.GameState.Value;
            int goalDifference = Math.Abs(gameState.BlueScore - gameState.RedScore);

            if (votes.Count > 0 && goalDifference < minGoalDeficit)
            {
                Plugin.Log.LogDebug($"Forfeit vote cancelled because the goal difference fell below the threshold.");
                uiChat.Server_SendSystemChatMessage($"{messagePrefix} Cancelling <b>forfeit</b> vote.");
                votes.Clear();
            }
        }
    }
}
