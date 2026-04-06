using UnityModManagerNet;

namespace ExtraTenders;

public class Settings : UnityModManager.ModSettings
{
    /// <summary>Multiplier applied to fuel loading speed for our aux cars.</summary>
    public float LoadMultiplier = 10f;

    /// <summary>Percentage of capacity our aux cars spawn with (0 = empty).</summary>
    public float StartingFuelPercent = 0f;

    public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);
}
