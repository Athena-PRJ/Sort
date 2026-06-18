using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sort.Monetization
{
    public enum IapProductKind
    {
        /// <summary>Buyable repeatedly; grants <see cref="IapProduct.coinAmount"/> coins each time.</summary>
        Coins,
        /// <summary>One-time purchase; sets the persistent "ads removed" flag.</summary>
        RemoveAds
    }

    [Serializable]
    public class IapProduct
    {
        [Tooltip("Store product ID — must match EXACTLY the ID created in Google Play / App Store / Unity dashboard.")]
        public string productId = "com.athena.sort.coins_small";

        [Tooltip("What buying this grants.")]
        public IapProductKind kind = IapProductKind.Coins;

        [Tooltip("Coins granted on purchase (Coins kind only).")]
        [Min(0)] public int coinAmount = 1000;

        [Tooltip("Optional label for your own store UI (the real localized title/price comes from the store).")]
        public string displayName = "Pile of Coins";

        /// <summary>Unity IAP product type: Consumable for coins (re-buyable), NonConsumable for remove-ads.</summary>
        public bool IsConsumable => kind == IapProductKind.Coins;
    }

    /// <summary>
    /// Designer-editable catalog of in-app products. Create via Assets ▸ Create ▸ Sort ▸ IAP Catalog,
    /// put it under Assets/Resources/ named exactly "IapCatalog" so <see cref="IapService"/> auto-loads it,
    /// then list each product with the SAME id you registered in the store consoles.
    /// </summary>
    [CreateAssetMenu(menuName = "Sort/IAP Catalog", fileName = "IapCatalog")]
    public class IapCatalog : ScriptableObject
    {
        [Tooltip("All purchasable products. IDs must match the store dashboards exactly.")]
        public List<IapProduct> products = new List<IapProduct>();

        static IapCatalog _instance;
        /// <summary>Auto-loaded from Resources/IapCatalog. Null if missing.</summary>
        public static IapCatalog Instance
        {
            get
            {
                if (_instance == null) _instance = Resources.Load<IapCatalog>("IapCatalog");
                return _instance;
            }
        }

        public IapProduct Find(string productId)
        {
            if (products == null) return null;
            for (int i = 0; i < products.Count; i++)
                if (products[i] != null && products[i].productId == productId) return products[i];
            return null;
        }
    }
}
