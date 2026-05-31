using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Dev-tunable economy parameters. Create one asset under Assets/Resources/ named "EconomyConfig"
    /// so PlayerEconomy can auto-load it. Editable in the Inspector.
    /// </summary>
    [CreateAssetMenu(menuName = "Sort/Economy Config", fileName = "EconomyConfig")]
    public class EconomyConfig : ScriptableObject
    {
        [Header("Lives")]
        [Min(1)] public int maxLives = 5;
        [Tooltip("Real-world hours between automatic life refills.")]
        [Min(0.01f)] public float lifeRefreshIntervalHours = 1f;

        [Header("Life purchase (one transaction adds X lives, costs Y coins)")]
        [Min(1)] public int livesPerPurchase = 5;
        [Min(0)] public int coinsPerLifePurchase = 100;

        [Header("Skill costs (coins per extra use after the free uses run out)")]
        [Min(0)] public int coinsPerRewindUse = 50;
    }
}
