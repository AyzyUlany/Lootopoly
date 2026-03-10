using UnityEngine;
using System.Runtime.InteropServices;

// ============================================================
// LOOTOPOLY – LootopolyUIBridge
// ============================================================
// Connects Unity C# logic directly to the HTML DOM's global
// JavaScript functions defined in game.js.
// ============================================================

public static class LootopolyUIBridge
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void JS_UpdateGameState(string json);
    [DllImport("__Internal")] private static extern void JS_LogEvent(string msg);
    [DllImport("__Internal")] private static extern void JS_ShowToast(string payload);
    [DllImport("__Internal")] private static extern void JS_OnDiceRolled(int result);
#else
    // Editor fallbacks so the game doesn't crash while testing outside of WebGL
    private static void JS_UpdateGameState(string json) { }
    private static void JS_LogEvent(string msg) { }
    private static void JS_ShowToast(string payload) { }
    private static void JS_OnDiceRolled(int result) { }
#endif

    public static void UpdateGameState(string json) => JS_UpdateGameState(json);
    public static void LogEvent(string msg) => JS_LogEvent(msg);
    public static void ShowToast(string payload) => JS_ShowToast(payload);
    public static void OnDiceRolled(int result) => JS_OnDiceRolled(result);
}