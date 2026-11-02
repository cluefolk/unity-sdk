# Cluefolk Unity SDK

In-game player support for Unity games on Android and iOS. Players chat with your support team without leaving the game. The package calls the native [Android](https://github.com/cluefolk/android-sdk) and [iOS](https://github.com/cluefolk/ios-sdk) SDKs, which have the support screen built in.

- One package for Unity 2022.3 LTS and later
- Works without sign-in: players start anonymously, linking a game account is optional
- Nothing to set up by hand in Gradle or Xcode

## Requirements

- Unity 2022.3 LTS or later with Android and/or iOS build support
- Android: **Minimum API Level** 24 (Android 7.0), **Target API Level** 34 or higher
- iOS: **Target minimum iOS Version** 15.0 or higher
- A Cluefolk project: copy its **Project ID** from **Settings → General** at [cluefolk.com](https://cluefolk.com)

The package stops the build with a clear message if the Player Settings are below these minimums.

## Install

In Unity open **Window → Package Manager**, press **+ → Add package from git URL…** and enter:

```
https://github.com/cluefolk/unity-sdk.git#0.3.1
```

Or add it to `Packages/manifest.json`:

```json
"com.cluefolk.sdk": "https://github.com/cluefolk/unity-sdk.git#0.3.1"
```

What happens in player builds:

- **Android**: the Gradle project gets `com.cluefolk:android-sdk` of the same version from Maven Central (listed in Unity's default Gradle settings) with its AndroidX dependencies, and AndroidX is switched on.
- **iOS**: the Xcode project gets `Cluefolk.xcframework`, linked into UnityFramework and embedded in the app.

## Quick start

```csharp
using CluefolkSDK;

Cluefolk.Event += (type, payload) => { if (type == "ready") Debug.Log("Support is ready"); };
Cluefolk.Initialize(new CluefolkOptions { ProjectId = "YOUR_PROJECT_ID" });
```

`Initialize` loads saved credentials in the background. When it is done, `Cluefolk.IsReady` is `true` and `Event` reports `ready`. Then a support button opens the chat:

```csharp
Cluefolk.Open();             // The support home screen.
Cluefolk.OpenConversation(); // Straight to the player's current chat.
```

Call the API from the Unity main thread. In the Editor and on desktop the support screen does not open; build to a device or simulator.

## Players

Support is anonymous by default: the native SDK recognises the device, so the history survives restarts. No game backend is needed.

### The game's player ID

Pass your own player ID so the support team can find the player. It is shown to the team as **not verified**.

```csharp
Cluefolk.SetPlayerId(GameApi.UserId); // null or "" clears it.
```

### Verified players (optional)

To link support to the game account, your game backend signs a short-lived identity token after the player signs in to the game: ES256 JWT, `typ: cluefolk-identity+jwt`, at most 5 minutes, `sub` = your player ID. Add the backend's public key in **Settings → Server connection → Players and sign-in**. Signing keys never go into the game.

```csharp
// After the game login succeeds; the device's anonymous history moves to the account.
await Cluefolk.LoginWithIdentityAsync(identityToken);

// Optional: let the SDK ask your backend for a fresh token when it needs a new session.
Cluefolk.SetIdentityTokenProvider(async () => await GameApi.GetCluefolkIdentityAsync());

// When the player signs out or switches accounts:
Cluefolk.Logout();
```

Do not fail the game login because of a support error. If your backend cannot provide a token, support still opens anonymously and `Event` reports `error` with `IDENTITY_UNAVAILABLE`.

## Context for the support team

```csharp
Cluefolk.SetAttributes(new Dictionary<string, object> { ["level"] = 37 });          // Keys registered in your project.
Cluefolk.SetConversationContext(new Dictionary<string, object> { ["screen"] = "shop" }); // Sent with the next new conversation.
```

## Appearance

The support screen's look ships inside your game build. In Cluefolk open **Settings → Widget**, press **Download for build** and put the `<ID>_widget.json` file into your project, for example as a `TextAsset`. Before opening support:

```csharp
Cluefolk.SetAppearance(new Appearance.Builder(themeFile.text).Build());
```

`new Appearance.Builder()` without a file starts from the default look; change it in code with `Title`, `Mode`, `Rotation`, `LightColors`/`DarkColors`, `Welcome`, `Topics` and `Contact`. `SetAppearance(null)` returns to the default look. The screen turns like the game unless the theme sets `Rotation` to `Free`.

## Push notifications

Notifications about support replies go through your game server and your existing push setup; Cluefolk never holds device tokens. Pass Cluefolk notifications to the package:

```csharp
Cluefolk.HandlePush(data);               // A notification arrived: updates the unread count.
Cluefolk.HandlePush(data, opened: true); // The player tapped it: opens the conversation.
```

`HandlePush` returns `false` for notifications that are not from Cluefolk. See [Push notifications](https://cluefolk.com/guide/en/#push) for the server side.

## Events

`Cluefolk.Event` reports `(type, JSON payload)`: `ready`, `opened`, `closed`, `unreadChanged` (`{"count":N}`), `conversationCreated`, `conversationStatusChanged`, `messageSent` and `error` (`{"code":"..."}`).

## Using your own Gradle or Xcode setup

- If the Gradle project cannot reach Maven Central, add a mirror to your custom settings template; the package adds `implementation 'com.cluefolk:android-sdk:0.3.1'` to `unityLibrary`.
- The iOS framework lives in the package's `Native~` folder and is copied into each Xcode build under `Frameworks/com.cluefolk.sdk`.

## More

- [Cluefolk guide: integrating the game client](https://cluefolk.com/guide/en/#unity)
- [Game server contract](https://cluefolk.com/game-integration.md)
- [Changelog](CHANGELOG.md) · [License: MIT](LICENSE.md)
