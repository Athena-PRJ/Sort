using System;
using UnityEngine;

namespace Sort
{
    /// <summary>
    /// Static facade over the player's coins & lives, persisted in PlayerPrefs.
    /// Auto-loads EconomyConfig from Resources on first access. Refreshes lives
    /// based on elapsed real-world time since the last refill.
    /// </summary>
    public static class PlayerEconomy
    {
        const string COINS_KEY = "Sort_Coins";
        const string LIVES_KEY = "Sort_Lives";
        const string LAST_REFRESH_KEY = "Sort_LastLifeRefresh";
        const string SWITCH_USES_KEY = "Sort_SwitchUses";
        const string MAGNET_USES_KEY = "Sort_MagnetUses";

        public static event Action Changed;

        static EconomyConfig _config;
        public static EconomyConfig Config
        {
            get
            {
                if (_config == null) _config = Resources.Load<EconomyConfig>("EconomyConfig");
                return _config;
            }
        }

        // ---------- Coins ----------

        public static int Coins
        {
            get => PlayerPrefs.GetInt(COINS_KEY, 0);
            private set
            {
                PlayerPrefs.SetInt(COINS_KEY, Mathf.Max(0, value));
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static void AddCoins(int amount)
        {
            if (amount <= 0) return;
            Coins = Coins + amount;
        }

        public static bool TrySpendCoins(int amount)
        {
            if (amount < 0) return false;
            if (Coins < amount) return false;
            Coins = Coins - amount;
            return true;
        }

        // ---------- Lives ----------

        public static int Lives
        {
            get
            {
                ApplyLifeRefresh();
                int max = Config != null ? Config.maxLives : 5;
                return Mathf.Clamp(PlayerPrefs.GetInt(LIVES_KEY, max), 0, max);
            }
            private set
            {
                int max = Config != null ? Config.maxLives : 5;
                PlayerPrefs.SetInt(LIVES_KEY, Mathf.Clamp(value, 0, max));
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static bool HasLives => Lives > 0;
        public static bool LivesAtMax => Config != null && Lives >= Config.maxLives;

        public static void DeductLife()
        {
            ApplyLifeRefresh();
            int cur = Lives;
            if (cur <= 0) return;
            Lives = cur - 1;
            // If we just dropped from max, start the refill clock now.
            if (Config != null && cur >= Config.maxLives)
                SetLastRefreshTime(DateTime.UtcNow);
        }

        public static bool TryBuyLives()
        {
            if (Config == null) return false;
            if (LivesAtMax) return false;
            if (!TrySpendCoins(Config.coinsPerLifePurchase)) return false;
            Lives = Mathf.Min(Lives + Config.livesPerPurchase, Config.maxLives);
            return true;
        }

        public static TimeSpan TimeUntilNextLifeRefresh
        {
            get
            {
                if (Config == null || LivesAtMax) return TimeSpan.Zero;
                var nextAt = GetLastRefreshTime() + TimeSpan.FromHours(Config.lifeRefreshIntervalHours);
                var remaining = nextAt - DateTime.UtcNow;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }

        static void ApplyLifeRefresh()
        {
            if (Config == null) return;
            int max = Config.maxLives;
            int cur = Mathf.Clamp(PlayerPrefs.GetInt(LIVES_KEY, max), 0, max);
            if (cur >= max) return;

            var last = GetLastRefreshTime();
            double interval = Config.lifeRefreshIntervalHours;
            if (interval <= 0) return;

            double elapsed = (DateTime.UtcNow - last).TotalHours;
            int refills = (int)Math.Floor(elapsed / interval);
            if (refills <= 0) return;

            int newLives = Mathf.Min(cur + refills, max);
            PlayerPrefs.SetInt(LIVES_KEY, newLives);
            // Advance the clock by the consumed refill ticks; keep the remainder.
            SetLastRefreshTime(last + TimeSpan.FromHours(refills * interval));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        static DateTime GetLastRefreshTime()
        {
            var s = PlayerPrefs.GetString(LAST_REFRESH_KEY, "");
            if (long.TryParse(s, out long ticks)) return new DateTime(ticks, DateTimeKind.Utc);
            var now = DateTime.UtcNow;
            SetLastRefreshTime(now);
            return now;
        }

        static void SetLastRefreshTime(DateTime time)
        {
            PlayerPrefs.SetString(LAST_REFRESH_KEY, time.Ticks.ToString());
            PlayerPrefs.Save();
        }

        // ---------- Skill use stockpile ----------
        // Player accumulates Switch/Magnet uses via purchase. Each successful skill use decrements
        // the count. When count reaches 0, SkillManager shows the BuyUsesPanel to buy 1 more.

        public static int SwitchUses
        {
            get => PlayerPrefs.GetInt(SWITCH_USES_KEY, 0);
            private set
            {
                PlayerPrefs.SetInt(SWITCH_USES_KEY, Mathf.Max(0, value));
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static int MagnetUses
        {
            get => PlayerPrefs.GetInt(MAGNET_USES_KEY, 0);
            private set
            {
                PlayerPrefs.SetInt(MAGNET_USES_KEY, Mathf.Max(0, value));
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static void AddSwitchUses(int amount) { if (amount > 0) SwitchUses = SwitchUses + amount; }
        public static void AddMagnetUses(int amount) { if (amount > 0) MagnetUses = MagnetUses + amount; }

        /// <summary>Consume one stored Switch use. Returns false if the player has none.</summary>
        public static bool TrySpendSwitchUse()
        {
            if (SwitchUses <= 0) return false;
            SwitchUses = SwitchUses - 1;
            return true;
        }

        /// <summary>Consume one stored Magnet use. Returns false if the player has none.</summary>
        public static bool TrySpendMagnetUse()
        {
            if (MagnetUses <= 0) return false;
            MagnetUses = MagnetUses - 1;
            return true;
        }

        /// <summary>Buy 1 Switch use with coins. Returns false if not enough coins.</summary>
        public static bool TryBuySwitchUse()
        {
            if (Config == null) return false;
            if (!TrySpendCoins(Config.coinsPerSwitchUse)) return false;
            AddSwitchUses(1);
            return true;
        }

        /// <summary>Buy 1 Magnet use with coins. Returns false if not enough coins.</summary>
        public static bool TryBuyMagnetUse()
        {
            if (Config == null) return false;
            if (!TrySpendCoins(Config.coinsPerMagnetUse)) return false;
            AddMagnetUses(1);
            return true;
        }

        // ---------- Dev / test ----------

        public static void ResetEconomy()
        {
            PlayerPrefs.DeleteKey(COINS_KEY);
            PlayerPrefs.DeleteKey(LIVES_KEY);
            PlayerPrefs.DeleteKey(LAST_REFRESH_KEY);
            PlayerPrefs.DeleteKey(SWITCH_USES_KEY);
            PlayerPrefs.DeleteKey(MAGNET_USES_KEY);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
