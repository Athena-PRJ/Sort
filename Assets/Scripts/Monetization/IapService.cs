// In-app purchasing wrapper around Unity IAP (com.unity.purchasing).
//
// The real store integration compiles ONLY when SORT_IAP is defined, so the project builds with no IAP
// package installed. Without the define this falls back to a MOCK that grants instantly in the Editor —
// so the buy-coins / remove-ads flows are testable now. To go live:
//   1. Window ▸ Package Manager ▸ install "In App Purchasing" (com.unity.purchasing) + enable it in Services.
//   2. Player Settings ▸ Scripting Define Symbols (Android + iOS): add  SORT_IAP
//   3. Create an IapCatalog asset under Assets/Resources/ (Assets ▸ Create ▸ Sort ▸ IAP Catalog) and list
//      products whose IDs match Google Play / App Store consoles exactly.
//   4. Drop an IapService on a GameObject in the first scene.
// See Assets/Scripts/Monetization/README_Monetization.md.

using System;
using UnityEngine;

namespace Sort.Monetization
{
    /// <summary>
    /// Shared, SDK-independent reward granting for purchases. Called by both the real and mock
    /// <c>IapService</c> so the "what a product gives the player" logic lives in exactly one place.
    /// </summary>
    internal static class IapRewards
    {
        public static void Grant(IapProduct product)
        {
            if (product == null) return;
            switch (product.kind)
            {
                case IapProductKind.Coins:
                    PlayerEconomy.AddCoins(product.coinAmount);
                    Debug.Log($"[IapService] Granted {product.coinAmount} coins ({product.productId}).");
                    break;
                case IapProductKind.RemoveAds:
                    PlayerEconomy.SetAdsRemoved(true);
                    Debug.Log($"[IapService] Ads removed ({product.productId}).");
                    break;
            }
        }
    }
}

#if SORT_IAP
namespace Sort.Monetization
{
    using UnityEngine.Purchasing;
    using UnityEngine.Purchasing.Extensions;

    /// <summary>Real Unity IAP implementation. Compiled when SORT_IAP is defined.</summary>
    public class IapService : MonoBehaviour, IDetailedStoreListener
    {
        public static IapService Instance { get; private set; }

        public bool IsInitialized => _controller != null;

        /// <summary>Fired after a successful purchase, with the catalog product that was bought.</summary>
        public event Action<IapProduct> PurchaseCompleted;
        /// <summary>Fired on a failed purchase. (productId, reason)</summary>
        public event Action<string, string> PurchaseFailed;

        IStoreController _controller;
        IExtensionProvider _extensions;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            Initialize();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Initialize()
        {
            var catalog = IapCatalog.Instance;
            if (catalog == null || catalog.products == null || catalog.products.Count == 0)
            {
                Debug.LogWarning("[IapService] No IapCatalog found in Resources or it is empty — IAP disabled.");
                return;
            }

            var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
            foreach (var p in catalog.products)
            {
                if (p == null || string.IsNullOrEmpty(p.productId)) continue;
                builder.AddProduct(p.productId, p.IsConsumable ? ProductType.Consumable : ProductType.NonConsumable);
            }
            UnityPurchasing.Initialize(this, builder);
        }

        /// <summary>Start a purchase of <paramref name="productId"/> (must be in the catalog + initialized).</summary>
        public void Buy(string productId)
        {
            if (_controller == null) { PurchaseFailed?.Invoke(productId, "not initialized"); return; }
            var product = _controller.products.WithID(productId);
            if (product == null || !product.availableToPurchase) { PurchaseFailed?.Invoke(productId, "unavailable"); return; }
            _controller.InitiatePurchase(product);
        }

        /// <summary>iOS requires an explicit restore button for non-consumables (e.g. Remove Ads).</summary>
        public void RestorePurchases()
        {
            if (_extensions == null) return;
            var apple = _extensions.GetExtension<IAppleExtensions>();
            apple?.RestoreTransactions((ok, msg) => Debug.Log($"[IapService] Restore: {ok} {msg}"));
        }

        // ----- IStoreListener / IDetailedStoreListener -----
        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
        {
            _controller = controller;
            _extensions = extensions;
            Debug.Log("[IapService] Initialized.");
        }

        public void OnInitializeFailed(InitializationFailureReason error) => OnInitializeFailed(error, null);

        public void OnInitializeFailed(InitializationFailureReason error, string message)
            => Debug.LogWarning($"[IapService] Init failed: {error} {message}");

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
        {
            string id = args.purchasedProduct.definition.id;
            var product = IapCatalog.Instance != null ? IapCatalog.Instance.Find(id) : null;
            if (product != null)
            {
                IapRewards.Grant(product);
                PurchaseCompleted?.Invoke(product);
            }
            else Debug.LogWarning($"[IapService] Purchased unknown product '{id}' — not in catalog.");

            return PurchaseProcessingResult.Complete;
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureDescription desc)
            => PurchaseFailed?.Invoke(product != null ? product.definition.id : "?", desc.reason.ToString());

        public void OnPurchaseFailed(Product product, PurchaseFailureReason reason)
            => PurchaseFailed?.Invoke(product != null ? product.definition.id : "?", reason.ToString());
    }
}
#else
namespace Sort.Monetization
{
    /// <summary>
    /// Mock IAP used until the Unity IAP package + SORT_IAP define are added. Keeps the same public API so
    /// game/UI code compiles and the grant flow is testable: in the Editor (or a dev build) Buy() grants
    /// immediately; on a real device with no SDK it reports failure instead of silently giving rewards.
    /// </summary>
    public class IapService : MonoBehaviour
    {
        public static IapService Instance { get; private set; }

        public bool IsInitialized => true;
        public event Action<IapProduct> PurchaseCompleted;
        public event Action<string, string> PurchaseFailed;

        [Tooltip("Editor/dev only: instantly grant the product when Buy() is called, so the flow is testable " +
                 "without the Unity IAP package. Ignored on device builds (reports failure there).")]
        [SerializeField] private bool simulatePurchasesInEditor = true;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public void Buy(string productId)
        {
            var product = IapCatalog.Instance != null ? IapCatalog.Instance.Find(productId) : null;
            if (product == null) { PurchaseFailed?.Invoke(productId, "not in catalog"); return; }

            if (Application.isEditor && simulatePurchasesInEditor)
            {
                Debug.Log($"[IapService] (Mock) Simulating purchase of '{productId}'.");
                IapRewards.Grant(product);
                PurchaseCompleted?.Invoke(product);
            }
            else
            {
                Debug.LogWarning("[IapService] (Mock) Unity IAP not installed (define SORT_IAP). Purchase denied.");
                PurchaseFailed?.Invoke(productId, "IAP not installed");
            }
        }

        public void RestorePurchases() => Debug.Log("[IapService] (Mock) RestorePurchases no-op.");
    }
}
#endif
