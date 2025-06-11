using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Unity.Collections;

namespace VoteForfeit;


public class VoteForfeit
{
    private const int MIN_GOAL_DEFICIT = 3;
    private const uint TIMEOUT_SECONDS = 60;

    private const string MESSAGE_PREFIX = "<color=orange><b>VoteForfeit</b></color>";
    private const string HELP_MESSAGE = $"commands:\n"
        + "* <b>/forfeit</b> (/ff) - Vote to forfeit";
    private const string RED_TEAM_TEXT = $"<color=red><b>RED</b></color>";
    private const string BLUE_TEAM_TEXT = $"<color=blue><b>BLUE</b></color>";


    private static void sendMessage(UIChat uiChat, object message)
    {
        uiChat.Server_SendSystemChatMessage($"{MESSAGE_PREFIX} {message}");
    }
    private static void sendMessage(UIChat uiChat, object message, ulong clientId)
    {
        uiChat.Server_SendSystemChatMessage($"{MESSAGE_PREFIX} {message}", clientId);
    }

    static readonly FieldInfo _uiChat = typeof(UIChatController).GetField("uiChat", BindingFlags.Instance | BindingFlags.NonPublic);

    private static Dictionary<FixedString32Bytes, DateTime> votes = [];

    [HarmonyPatch(typeof(UIChatController), "Event_Server_OnChatCommand")]
    public static class UIChatControllerEventServerOnChatCommandPatch
    {

        [HarmonyPrefix]
        public static void Event_Server_OnChatCommand(
            UIChatController __instance,
            Dictionary<string, object> message)
        {
            if (__instance == null) return;
            if (message == null) return;
            if (!message.ContainsKey("command")) return;
            if (!message.ContainsKey("clientId")) return;

            PlayerManager playerManager = NetworkBehaviourSingleton<PlayerManager>.Instance;
            GameManager gameManager = NetworkBehaviourSingleton<GameManager>.Instance;
            UIChat uiChat = (UIChat)_uiChat.GetValue(__instance);

            if (playerManager == null) return;
            if (gameManager == null) return;
            if (uiChat == null) return;

            string command = message["command"].ToString().ToLower();
            ulong clientId = (ulong)message["clientId"];

            if (command.Equals("/help")) { sendMessage(uiChat, HELP_MESSAGE, clientId); return; }
            if (!(command.Equals("/forfeit") || command.Equals("/ff"))) return;

            DateTime now = DateTime.Now;
            GameState gameState = gameManager.GameState.Value;

            Player player = playerManager.GetPlayerByClientId(clientId);
            FixedString32Bytes steamId = player.SteamId.Value;
            PlayerTeam team = player.Team.Value;
            List<Player> teammates = playerManager.GetPlayersByTeam(team);
            int needed = teammates.Count - 1;

            // Check for valid game phase.
            if (gameState.Phase is GamePhase.None or GamePhase.Warmup or GamePhase.GameOver)
            {
                Mod.LogDebug($"{clientId} tried to forfeit, but the game phase was {gameState.Phase}.");
                sendMessage(uiChat,
                    "You cannot vote to forfeit when there is no match.",
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
                    teamText = RED_TEAM_TEXT;
                    break;
                case PlayerTeam.Blue:
                    goalDeficit = gameState.RedScore - gameState.BlueScore;
                    teamText = BLUE_TEAM_TEXT;
                    break;
                default:
                    Mod.LogDebug($"{clientId} tried to forfeit, but they were not on a team.");
                    sendMessage(uiChat, "You must be in a team to forfeit.", clientId);
                    return;
            }

            // Check goal deficit.
            if (goalDeficit < MIN_GOAL_DEFICIT)
            {
                Mod.LogDebug($"{clientId} tried to forfeit, but the goal deficit was not large enough: {goalDeficit}<{MIN_GOAL_DEFICIT}.");
                sendMessage(uiChat,
                    $"You must be losing by at least {MIN_GOAL_DEFICIT} goals to forfeit.",
                    clientId
                );
                return;
            }

            // Requirements passed, add vote.
            votes = votes
                .Where(pair =>
                {
                    bool recent = now.Subtract(pair.Value).Seconds < TIMEOUT_SECONDS;
                    Player player = playerManager.GetPlayerBySteamId(pair.Key);
                    bool onTheTeam = player != null && player.Team.Value == team;
                    return recent && onTheTeam;
                })
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            bool alreadyVoted = votes.ContainsKey(steamId);
            votes[steamId] = now;

            if (votes.Count >= needed)
            {
                Mod.LogDebug($"Forfeit vote passed.");
                sendMessage(uiChat, $"{teamText} has forfeited the match.");
                votes.Clear();
                gameManager.Server_GameOver();
            }
            else if (!alreadyVoted)
            {
                Mod.LogDebug($"Forfeit vote in progress [{votes.Count}/{needed}].");
                sendMessage(uiChat, $"Vote <b>forfeit</b> in progress ({votes.Count}/{needed}).");
            }
            else
            {
                Mod.LogDebug($"{clientId} tried to forfeit, but they already voted recently.");
                sendMessage(uiChat, "You already recently voted to forfeit.", clientId);
            }
        }
    }

    [HarmonyPatch(typeof(UIChatController), "Event_OnGoalScored")]
    public static class UIChatControllerEventOnGoalScoredPatch
    {
        [HarmonyPrefix]
        public static void Event_OnGoalScored(
            UIChatController __instance,
            Dictionary<string, object> message)
        {
            if (__instance == null) return;

            GameManager gameManager = NetworkBehaviourSingleton<GameManager>.Instance;
            UIChat uiChat = (UIChat)_uiChat.GetValue(__instance);

            if (gameManager == null) return;
            if (uiChat == null) return;

            GameState gameState = gameManager.GameState.Value;
            int goalDifference = Math.Abs(gameState.BlueScore - gameState.RedScore);

            if (votes.Count > 0 && goalDifference < MIN_GOAL_DEFICIT)
            {
                Mod.LogDebug($"Forfeit vote cancelled because the goal difference fell below the threshold.");
                sendMessage(uiChat, "Cancelling <b>forfeit</b> vote.");
                votes.Clear();
            }
        }
    }
}
