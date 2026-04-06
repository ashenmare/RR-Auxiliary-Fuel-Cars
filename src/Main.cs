using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace ExtraTenders;

public static class Main
{
    public static UnityModManager.ModEntry Mod { get; private set; } = null!;
    public static Settings Settings { get; private set; } = null!;

    public static bool Load(UnityModManager.ModEntry modEntry)
    {
        Mod = modEntry;
        Settings = UnityModManager.ModSettings.Load<Settings>(modEntry);
        modEntry.OnGUI    = OnGUI;
        modEntry.OnSaveGUI = OnSaveGUI;
        CarDefinitions.Install();
        new Harmony(modEntry.Info.Id).PatchAll();
        Mod.Logger.Log("Extra Tenders loaded.");
        return true;
    }

    static void OnGUI(UnityModManager.ModEntry modEntry)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            $"Fuel loading speed multiplier: {Settings.LoadMultiplier:F0}x",
            GUILayout.Width(260));
        Settings.LoadMultiplier = Mathf.Round(
            GUILayout.HorizontalSlider(Settings.LoadMultiplier, 1f, 20f,
                GUILayout.Width(200)));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label(
            $"Starting fuel on placement: {Settings.StartingFuelPercent:F0}%",
            GUILayout.Width(260));
        Settings.StartingFuelPercent = Mathf.Round(
            GUILayout.HorizontalSlider(Settings.StartingFuelPercent, 0f, 100f,
                GUILayout.Width(200)));
        GUILayout.EndHorizontal();
    }

    static void OnSaveGUI(UnityModManager.ModEntry modEntry) => Settings.Save(modEntry);
}
