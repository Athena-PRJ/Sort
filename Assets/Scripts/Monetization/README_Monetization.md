# Monetization (Ads + IAP) — setup

Scaffold added 2026-06-17. **It compiles and runs today with no SDK installed** — ads use Mock providers
that resolve instantly, IAP simulates purchases in the Editor. Go live by installing the SDKs + adding a
define symbol; no game code changes needed.

Ad network = **Unity LevelPlay** (`com.unity.services.levelplay`). The legacy Unity Ads network was retired
for monetization on 2026-01-31, so LevelPlay is the supported path. (Legacy `com.unity.ads` is not used.)

## Files (`Assets/Scripts/Monetization/`)

| File | Role |
|---|---|
| `IRewardedAdProvider.cs` / `IInterstitialAdProvider.cs` | SDK-agnostic ad interfaces. AdsService talks only to these. |
| `MockRewardedAdProvider.cs` / `MockInterstitialAdProvider.cs` | No-SDK providers; resolve instantly. Default until a real SDK is compiled. |
| `AdsService.cs` | **The only thing game code calls.** Singleton; `ShowContinueAd` / `ShowCoinsAd` / `MaybeShowInterstitial`. App Key + Ad Unit IDs + placements + interstitial frequency are Inspector fields. |
| `LevelPlaySdk.cs` | Shared `LevelPlay.Init` (called once for both ad types). `#if SORT_ADS_LEVELPLAY`. |
| `LevelPlayRewardedProvider.cs` / `LevelPlayInterstitialProvider.cs` | Real LevelPlay providers. `#if SORT_ADS_LEVELPLAY`. |
| `IapCatalog.cs` | ScriptableObject listing products (coin packs, remove-ads). |
| `IapService.cs` | Unity IAP wrapper (real with `SORT_IAP`, mock without). Grants via `PlayerEconomy`. |

## Where it's wired into the game

- **In-game continue** — `GameManager.ContinueWithAd()` → `AdsService.ShowContinueAd(...)`; grants the
  continue **only on reward earned** (falls back to immediate grant if no AdsService in scene).
- **Free coins (MainMenu)** — `CurrencyHud.WatchAdForCoins()` (hook the `watchAdButton` field or its
  `OnClick`). Coins credited by `AdsService.ShowCoinsAd()` on success.
- **Between-levels interstitial** — `GameManager.NextLevel()` → `AdsService.MaybeShowInterstitial(proceed)`;
  shows when due (frequency) + ready + player hasn't bought Remove Ads, then navigates. Always navigates.
- **IAP grants** — `IapService` → `PlayerEconomy.AddCoins` (coins) or `PlayerEconomy.SetAdsRemoved(true)`
  (remove-ads). `PlayerEconomy.AdsRemoved` suppresses interstitials (rewarded ads stay available — opt-in).

## Scene setup (works now with mocks)

1. In your **first** scene (MainMenu), create an empty GameObject `Services`.
2. Add `AdsService` and `IapService`. They `DontDestroyOnLoad` — add once.
3. On `AdsService`: leave `Force Mock Provider` ON for Editor testing; set `Coins Per Rewarded Ad` and
   `Interstitial Every N Calls`.
4. Wire the "Watch Ad for coins" button → `CurrencyHud.watchAdButton` (or `OnClick → WatchAdForCoins`).
5. The Out-of-Moves "Play on (Ads)" button → `GameManager.ContinueWithAd`.

## Going live — Ads (LevelPlay)

1. Package Manager → install **Ads Mediation** (`com.unity.services.levelplay`, v9+). Open
   **Ads Mediation ▸ LevelPlay Network Manager** and install the network adapters you want. Enable EDM4U
   Android auto-resolution when prompted.
2. Player Settings → **Scripting Define Symbols** (Android **and** iOS): add `SORT_ADS_LEVELPLAY`.
3. On the `AdsService` component, fill `Level Play App Key`, `Rewarded Ad Unit Id`, and (optional)
   `Interstitial Ad Unit Id`. Placement fields map to LevelPlay placement names (leave blank to show
   without a named placement).
4. Turn **OFF** `Force Mock Provider`.

> Other networks (AppLovin MAX, AdMob): implement `IRewardedAdProvider` / `IInterstitialAdProvider`, guard
> behind your own define, and add a branch in `AdsService.CreateRewardedProvider` / `CreateInterstitialProvider`.

## Going live — IAP (Unity IAP)

1. Package Manager → install **In App Purchasing** (`com.unity.purchasing`) + enable in Services.
2. Player Settings → Scripting Define Symbols (Android + iOS): add `SORT_IAP`.
3. Assets → Create → **Sort → IAP Catalog**; save as `Assets/Resources/IapCatalog.asset`. Add products whose
   `productId` matches Google Play / App Store consoles **exactly** (Coins = Consumable, RemoveAds = NonConsumable).
4. Call `IapService.Instance.Buy("<productId>")` from store buttons. iOS: add a button → `RestorePurchases()`.

## Notes

- Rewarded ads stay available even after "Remove Ads" — they're opt-in. `AdsRemoved` gates interstitials
  (and any banners you add later).
- The economy uses the device clock; ads/IAP only call `PlayerEconomy.AddCoins` / `SetAdsRemoved`.
