using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace CluefolkSDK {
/// <summary>Only ProjectId is required. The widget is built into the native
/// SDKs. The logo is the game's app icon unless LogoResource names an Android
/// drawable/mipmap or an iOS bundle image (the same name on both, or set it per
/// platform).</summary>
public sealed class CluefolkOptions {
  public string ProjectId;
  public string ApiUrl = "https://cluefolk.com";
  public string LogoResource;
}

public static class Cluefolk {
  public const int BridgeProtocolVersion = 2;
  /// <summary>The package version; Android builds use the native SDK of the same version.</summary>
  public const string Version = "0.3.0";
  /// <summary>Native events as (type, JSON payload): ready, opened, closed,
  /// unreadChanged, conversationCreated, conversationStatusChanged,
  /// messageSent, error. An error payload is {"code":"..."}.</summary>
  public static event Action<string, string> Event = delegate {};
  private static readonly Dictionary<string, TaskCompletionSource<string>>
      Pending = new Dictionary<string, TaskCompletionSource<string>>();
  private static Func<Task<string>> identityProvider;
  private static bool initializationStarted = false;
  public static bool IsReady { get; private set; }
  private static int generation;
  private static int unityThread;

  public static void Initialize(CluefolkOptions options) {
    if (initializationStarted)
      throw new InvalidOperationException("Cluefolk is already initialized.");
    if (options == null || string.IsNullOrWhiteSpace(options.ProjectId))
      throw new ArgumentException("Project is required.");
    ValidateOrigin(options.ApiUrl);
    if (options.LogoResource != null && (options.LogoResource.Length > 128 || options.LogoResource.IndexOfAny(new[] {'/', '\\'}) >= 0))
      throw new ArgumentException("LogoResource must be a resource name.");
    var dispatcher = new GameObject("CluefolkDispatcher");
    UnityEngine.Object.DontDestroyOnLoad(dispatcher);
    dispatcher.AddComponent<CluefolkDispatcher>();
    unityThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
#if UNITY_IOS && !UNITY_EDITOR
    NativeInitialize(options.ProjectId, options.ApiUrl, options.LogoResource ?? "",
                     dispatcher.name);
#elif UNITY_ANDROID && !UNITY_EDITOR
    Android("initialize", options.ProjectId, options.ApiUrl, options.LogoResource ?? "",
            dispatcher.name);
#else
    UnityEngine.Object.Destroy(dispatcher);
    throw new PlatformNotSupportedException(
        "Cluefolk support is available in iOS and Android players.");
#endif
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
    initializationStarted = true;
#endif
  }
  /// <summary>Optional. Support works anonymously without a game identity.
  /// The provider returns a fresh identity JWT from the game backend; if it
  /// throws or returns an empty token, the player stays anonymous and Event
  /// reports error code IDENTITY_UNAVAILABLE. Passing null removes it.</summary>
  public static void SetIdentityTokenProvider(Func<Task<string>> provider) {
    Check(requireReady: false);
    identityProvider = provider;
    // The native SDK asks for a token only while a provider exists, so games
    // without one never report IDENTITY_UNAVAILABLE. null removes it.
#if UNITY_IOS && !UNITY_EDITOR
    NativeSetIdentityProvider(provider != null ? 1 : 0);
#elif UNITY_ANDROID && !UNITY_EDITOR
    Android("setIdentityProvider", provider != null);
#endif
  }
  /// <summary>Optional. Links support to the game account after game login;
  /// the server merges this install's anonymous conversations into it.</summary>
  public static async Task LoginWithIdentityAsync(string identityJwt) {
    Check();
    if (string.IsNullOrWhiteSpace(identityJwt))
      throw new ArgumentException(
          "An identity JWT from game login is required.");
    CancelPending();
    generation++;
    await Request(id => {
#if UNITY_IOS && !UNITY_EDITOR
      NativeLogin(identityJwt, id);
#elif UNITY_ANDROID && !UNITY_EDITOR
      Android("login", identityJwt, id);
#endif
    });
  }
  public static void Open() {
    Check();
#if UNITY_IOS && !UNITY_EDITOR
    NativeOpen();
#elif UNITY_ANDROID && !UNITY_EDITOR
    Android("open");
#endif
  }
  public static void SetLanguage(string language) {
    Check(requireReady: false);
    if (language == null || language.Length > 64 ||
        !System.Text.RegularExpressions.Regex.IsMatch(language, "^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8})*$"))
      throw new ArgumentException("Invalid language tag.");
#if UNITY_IOS && !UNITY_EDITOR
    NativeLanguage(language);
#elif UNITY_ANDROID && !UNITY_EDITOR
    Android("language", language);
#endif
  }
  /// <summary>Optional. The game's own player ID (for example its user ID).
  /// Staff see it on an anonymous player's conversations marked unverified;
  /// verified players are unaffected. Trimmed; null or empty clears it. A
  /// value over 256 characters or with CR, LF or NUL is ignored and Event
  /// reports error INVALID_ARGUMENT. Kept until Logout(); may be set before
  /// the ready event.</summary>
  public static void SetPlayerId(string playerId) {
    Check(requireReady: false);
#if UNITY_IOS && !UNITY_EDITOR
    NativeSetPlayerId(playerId ?? "");
#elif UNITY_ANDROID && !UNITY_EDITOR
    Android("playerId", playerId ?? "");
#endif
  }
  public static bool HandlePush(Dictionary<string, string> data, bool opened = false) {
    Check(requireReady: false);
    if (data == null) throw new ArgumentNullException(nameof(data));
    string json = Json(data);
#if UNITY_IOS && !UNITY_EDITOR
    return NativePush(json, opened ? 1 : 0) != 0;
#elif UNITY_ANDROID && !UNITY_EDITOR
    using (var bridge = new AndroidJavaClass("com.cluefolk.sdk.UnityBridge"))
      return bridge.CallStatic<bool>("handlePush", json, opened);
#else
    GC.KeepAlive(json); GC.KeepAlive(opened); return false;
#endif
  }
  /// <summary>Opens a conversation by id. An empty id opens the player's
  /// current chat; the first message there starts a conversation.</summary>
  public static void OpenConversation(string conversationId = "") {
    Check();
    if (conversationId == null || conversationId.Length > 128) throw new ArgumentException("Invalid support destination.");
#if UNITY_IOS && !UNITY_EDITOR
    NativeOpenConversation(conversationId);
#elif UNITY_ANDROID && !UNITY_EDITOR
    Android("openConversation", conversationId);
#endif
  }
  public static void Close() {
    Check();
#if UNITY_IOS && !UNITY_EDITOR
    NativeClose();
#elif UNITY_ANDROID && !UNITY_EDITOR
    Android("close");
#endif
  }
  public static void Logout() {
    Check();
    generation++;
    CancelPending();
#if UNITY_IOS && !UNITY_EDITOR
    NativeLogout();
#elif UNITY_ANDROID && !UNITY_EDITOR
    Android("logout");
#endif
  }
  /// <summary>The widget theme built with Appearance.Builder, usually from the
  /// file shipped with the build; it also decides how the support screen turns.
  /// Applies on the next Open(); null restores the default theme. The native SDK
  /// checks it: an invalid theme keeps the previous one and Event reports error
  /// INVALID_APPEARANCE. The server never sends a theme.</summary>
  public static void SetAppearance(Appearance appearance) {
    Check();
    // Two empty strings mean null; a built Appearance always has overrides ("{}" at least).
    string baseJson = appearance?.BaseJson ?? "";
    string overrides = appearance?.Overrides ?? "";
#if UNITY_IOS && !UNITY_EDITOR
    NativeSetAppearance(baseJson, overrides);
#elif UNITY_ANDROID && !UNITY_EDITOR
    Android("appearance", baseJson, overrides);
#else
    GC.KeepAlive(baseJson);
    GC.KeepAlive(overrides);
#endif
  }
  public static void
  SetConversationContext(Dictionary<string, object> context) {
    Check();
    var json = Json(context);
#if UNITY_IOS && !UNITY_EDITOR
    NativeContext(json);
#elif UNITY_ANDROID && !UNITY_EDITOR
    Android("context", json);
#else
    GC.KeepAlive(json);
#endif
  }
  public static void SetAttributes(Dictionary<string, object> attributes) {
    Check();
    if (attributes.Count > 64)
      throw new ArgumentException("At most 64 keys per batch.");
    var json = Json(attributes);
#if UNITY_IOS && !UNITY_EDITOR
    NativeAttributes(json);
#elif UNITY_ANDROID && !UNITY_EDITOR
    Android("attributes", json);
#else
    GC.KeepAlive(json);
#endif
  }
  public static void UnsetAttributes(string[] keys) {
    Check();
    if (keys == null || keys.Length > 64)
      throw new ArgumentException("At most 64 keys per batch.");
#if UNITY_IOS && !UNITY_EDITOR
    NativeUnset(Json(keys));
#elif UNITY_ANDROID && !UNITY_EDITOR
    Android("unset", (object)keys);
#endif
  }
  public static async Task<int> GetUnreadCountAsync() {
    Check();
    var result = await Request(id => {
#if UNITY_IOS && !UNITY_EDITOR
      NativeUnread(id);
#elif UNITY_ANDROID && !UNITY_EDITOR
      Android("unread", id);
#endif
    });
    return JsonUtility.FromJson<Unread>(result).count;
  }
  private static async Task<string> Request(Action<string> send) {
    var id = Guid.NewGuid().ToString();
    var completion = new TaskCompletionSource<string>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    Pending.Add(id, completion);
    try {
      send(id);
      if (await Task.WhenAny(completion.Task, Task.Delay(30000)) !=
          completion.Task)
        throw new TimeoutException("Cluefolk request timed out.");
      return await completion.Task;
    } finally {
      Pending.Remove(id);
    }
  }
  private static void CancelPending() {
    var list = new List<TaskCompletionSource<string>>(Pending.Values);
    Pending.Clear();
    foreach (var task in list)
      task.TrySetException(
          new OperationCanceledException("The game account changed."));
  }
  internal static void Receive(string json) {
    var message = JsonUtility.FromJson<Envelope>(json);
    if (message.type == "completion") {
      if (Pending.TryGetValue(message.requestId, out var pending)) {
        if (string.IsNullOrEmpty(message.error))
          pending.TrySetResult(message.payload);
        else
          pending.TrySetException(new InvalidOperationException(message.error));
      }
      return;
    }
    if (message.type == "identityTokenRequired" &&
        !string.IsNullOrEmpty(message.requestId)) {
      _ = ProvideIdentity(message.requestId);
      return;
    }
    if (message.type == "ready")
      IsReady = true;
    // Plugin-side failures (invalid appearance, context...) carry the
    // code in the envelope; expose them like native SDK errors.
    if (message.type == "error" && !string.IsNullOrEmpty(message.error))
      message.payload = Json(new Dictionary<string, string> { ["code"] = message.error });
    Event(message.type, message.payload);
  }
  private static async Task ProvideIdentity(string id) {
    int expected = generation;
    string token = "";
    string error = "";
    try {
      if (identityProvider == null)
        throw new InvalidOperationException();
      token = await identityProvider();
      if (string.IsNullOrWhiteSpace(token))
        throw new InvalidOperationException();
    } catch {
      error = "IDENTITY_PROVIDER_FAILED";
    }
    if (generation != expected)
      return;
#if UNITY_IOS && !UNITY_EDITOR
    NativeIdentity(id, token, error);
#elif UNITY_ANDROID && !UNITY_EDITOR
    Android("completeIdentity", id, token, error);
#else
    GC.KeepAlive(token);
    GC.KeepAlive(error);
#endif
  }
  private static void Check(bool requireReady = true) {
    if (!initializationStarted)
      throw new InvalidOperationException(
          "Initialize Cluefolk in a supported player first.");
    if (System.Threading.Thread.CurrentThread.ManagedThreadId != unityThread)
      throw new InvalidOperationException(
          "Call Cluefolk on the Unity main thread.");
    if (requireReady && !IsReady)
      throw new InvalidOperationException("Wait for Cluefolk's ready event.");
  }
  private static void ValidateOrigin(string raw) {
    if (!Uri.TryCreate(raw, UriKind.Absolute, out var url) ||
        url.Scheme != "https" || !string.IsNullOrEmpty(url.UserInfo) ||
        !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment))
      throw new ArgumentException("Cluefolk requires HTTPS URLs without " +
                                  "credentials or query parameters.");
  }
  private static string Json(object value) {
    var result = new StringBuilder();
    WriteJson(result, value, 0);
    var json = result.ToString();
    if (Encoding.UTF8.GetByteCount(json) > 15000)
      throw new ArgumentException("Cluefolk JSON exceeds its batch limit.");
    return json;
  }
  private static void WriteJson(StringBuilder output, object value, int depth) {
    if (depth > 8)
      throw new ArgumentException("JSON nesting is too deep.");
    if (value == null) {
      output.Append("null");
      return;
    }
    if (value is string text) {
      Quote(output, text);
      return;
    }
    if (value is bool boolean) {
      output.Append(boolean ? "true" : "false");
      return;
    }
    if (value is decimal exact) {
      Quote(output, exact.ToString(CultureInfo.InvariantCulture));
      return;
    }
    if (value is DateTime date) {
      Quote(output,
            date.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
      return;
    }
    if (value is IDictionary dictionary) {
      output.Append('{');
      bool first = true;
      foreach (DictionaryEntry pair in dictionary) {
        if (!(pair.Key is string key))
          throw new ArgumentException("JSON keys must be strings.");
        if (!first)
          output.Append(',');
        first = false;
        Quote(output, key);
        output.Append(':');
        WriteJson(output, pair.Value, depth + 1);
      }
      output.Append('}');
      return;
    }
    if (value is IEnumerable list) {
      output.Append('[');
      bool first = true;
      foreach (var item in list) {
        if (!first)
          output.Append(',');
        first = false;
        WriteJson(output, item, depth + 1);
      }
      output.Append(']');
      return;
    }
    if (value is byte || value is sbyte || value is short || value is ushort ||
        value is int || value is uint || value is long || value is ulong ||
        value is float || value is double) {
      var raw = Convert.ToString(value, CultureInfo.InvariantCulture);
      if (raw == "NaN" || raw == "Infinity" || raw == "-Infinity")
        throw new ArgumentException("JSON numbers must be finite.");
      output.Append(raw);
      return;
    }
    throw new ArgumentException("Unsupported JSON value.");
  }
  internal static void Quote(StringBuilder output, string value) {
    output.Append('"');
    foreach (char c in value) {
      switch (c) {
      case '"':
        output.Append("\\\"");
        break;
      case '\\':
        output.Append("\\\\");
        break;
      case '\n':
        output.Append("\\n");
        break;
      case '\r':
        output.Append("\\r");
        break;
      case '\t':
        output.Append("\\t");
        break;
      default:
        if (c < 32)
          output.Append("\\u" + ((int)c).ToString("x4"));
        else
          output.Append(c);
        break;
      }
    }
    output.Append('"');
  }
  [Serializable]
  private sealed class Envelope {
    public string type = "";
    public string requestId = "";
    public string error = "";
    public string payload = "";
  }
  [Serializable]
  private sealed class Unread {
    public int count = 0;
  }
#if UNITY_ANDROID && !UNITY_EDITOR
  private static void Android(string method, params object[] args) {
    using (var bridge = new AndroidJavaClass("com.cluefolk.sdk.UnityBridge"))
        bridge.CallStatic(method, args);
  }
#endif
#if UNITY_IOS && !UNITY_EDITOR
  [DllImport("__Internal", EntryPoint = "CLF_Initialize")]
  private static extern void NativeInitialize(string project, string api,
                                              string logo, string receiver);
  [DllImport("__Internal", EntryPoint = "CLF_Login")]
  private static extern void NativeLogin(string token, string requestId);
  [DllImport("__Internal", EntryPoint = "CLF_SetIdentityProvider")]
  private static extern void NativeSetIdentityProvider(int enabled);
  [DllImport("__Internal", EntryPoint = "CLF_Identity")]
  private static extern void NativeIdentity(string requestId, string token,
                                            string error);
  [DllImport("__Internal", EntryPoint = "CLF_Open")]
  private static extern void NativeOpen();
  [DllImport("__Internal", EntryPoint = "CLF_Language")]
  private static extern void NativeLanguage(string language);
  [DllImport("__Internal", EntryPoint = "CLF_SetPlayerId")]
  private static extern void NativeSetPlayerId(string playerId);
  [DllImport("__Internal", EntryPoint = "CLF_Push")]
  private static extern int NativePush(string json, int opened);
  [DllImport("__Internal", EntryPoint = "CLF_OpenConversation")]
  private static extern void NativeOpenConversation(string id);
  [DllImport("__Internal", EntryPoint = "CLF_Close")]
  private static extern void NativeClose();
  [DllImport("__Internal", EntryPoint = "CLF_Logout")]
  private static extern void NativeLogout();
  [DllImport("__Internal", EntryPoint = "CLF_SetAppearance")]
  private static extern void NativeSetAppearance(string baseJson, string overrides);
  [DllImport("__Internal", EntryPoint = "CLF_Context")]
  private static extern void NativeContext(string json);
  [DllImport("__Internal", EntryPoint = "CLF_Attributes")]
  private static extern void NativeAttributes(string json);
  [DllImport("__Internal", EntryPoint = "CLF_Unset")]
  private static extern void NativeUnset(string json);
  [DllImport("__Internal", EntryPoint = "CLF_Unread")]
  private static extern void NativeUnread(string requestId);
#endif
}
/// <summary>Color mode of the support widget: follow the device, or always
/// light or dark.</summary>
public enum AppearanceMode { System, Light, Dark }

/// <summary>How the support screen turns. Game: like the game (landscape,
/// portrait or both). Free: upright or sideways regardless of the game; on iOS
/// within the orientations the app allows.</summary>
public enum AppearanceRotation { Game, Free }

/// <summary>A shortcut of the topics block. Category is a conversation
/// category key of the project; Icon is up to 32 characters (for example an
/// emoji) or a PNG or JPEG data URL.</summary>
public sealed class AppearanceTopic {
  public string Label { get; }
  public string Category { get; }
  public string Icon { get; }
  public AppearanceTopic(string label, string category, string icon) {
    Label = label ?? throw new ArgumentNullException(nameof(label));
    Category = category ?? throw new ArgumentNullException(nameof(category));
    Icon = icon ?? throw new ArgumentNullException(nameof(icon));
  }
}

/// <summary>The support widget's theme for Cluefolk.SetAppearance: title,
/// color mode, light and dark colors, home screen blocks and how the support
/// screen turns. Build it from the file downloaded in Settings → Widget →
/// Download for build (for example a TextAsset), from the default theme, or
/// either with changes in code. The native SDK checks the result when it is
/// set, the way the widget does.</summary>
public sealed class Appearance {
  // The theme file ("" for the default theme) and the changes made in code as
  // JSON, which the native SDK applies with its own Appearance.Builder.
  internal readonly string BaseJson;
  internal readonly string Overrides;

  private Appearance(string baseJson, string overrides) {
    BaseJson = baseJson;
    Overrides = overrides;
  }

  /// <summary>Setters replace the block of their type or add it, keeping the
  /// order welcome, topics, contact.</summary>
  public sealed class Builder {
    private readonly string baseJson;
    private string title;
    private string mode;
    private string rotation;
    private string[] lightColors;
    private string[] darkColors;
    private string[] welcome;
    private string topicsTitle;
    private AppearanceTopic[] topics;
    private string[] contact;

    /// <summary>json is the text of the file from Settings → Widget →
    /// Download for build; null starts from the default theme.</summary>
    public Builder(string json = null) {
      if (json != null && json.Length == 0)
        throw new ArgumentException("The appearance JSON is empty; pass null for the default theme.", nameof(json));
      baseJson = json ?? "";
    }

    public Builder Title(string title) {
      this.title = title ?? throw new ArgumentNullException(nameof(title));
      return this;
    }

    public Builder Mode(AppearanceMode mode) {
      this.mode = mode switch {
        AppearanceMode.System => "system",
        AppearanceMode.Light => "light",
        AppearanceMode.Dark => "dark",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
      };
      return this;
    }

    public Builder Rotation(AppearanceRotation rotation) {
      this.rotation = rotation switch {
        AppearanceRotation.Game => "game",
        AppearanceRotation.Free => "free",
        _ => throw new ArgumentOutOfRangeException(nameof(rotation)),
      };
      return this;
    }

    /// <summary>Five #RRGGBB colors of the light theme; the widget derives the rest.</summary>
    public Builder LightColors(string accent, string action, string background, string surface, string text) {
      lightColors = Colors(accent, action, background, surface, text);
      return this;
    }

    /// <summary>Five #RRGGBB colors of the dark theme; the widget derives the rest.</summary>
    public Builder DarkColors(string accent, string action, string background, string surface, string text) {
      darkColors = Colors(accent, action, background, surface, text);
      return this;
    }

    public Builder Welcome(string title, string text) {
      welcome = new[] { title ?? throw new ArgumentNullException(nameof(title)),
                        text ?? throw new ArgumentNullException(nameof(text)) };
      return this;
    }

    /// <summary>Up to 12 topics; each opens a new conversation in its category.</summary>
    public Builder Topics(string title, params AppearanceTopic[] items) {
      if (items == null)
        throw new ArgumentNullException(nameof(items));
      foreach (var item in items)
        if (item == null)
          throw new ArgumentNullException(nameof(items));
      topicsTitle = title ?? throw new ArgumentNullException(nameof(title));
      topics = (AppearanceTopic[])items.Clone();
      return this;
    }

    public Builder Contact(string title, string text) {
      contact = new[] { title ?? throw new ArgumentNullException(nameof(title)),
                        text ?? throw new ArgumentNullException(nameof(text)) };
      return this;
    }

    /// <summary>Records the theme for SetAppearance. The native SDK checks it
    /// there; a failure is reported as error INVALID_APPEARANCE.</summary>
    public Appearance Build() {
      var fields = new List<string>();
      if (title != null)
        fields.Add(Field("title", JsonString(title)));
      if (mode != null)
        fields.Add(Field("mode", JsonString(mode)));
      if (rotation != null)
        fields.Add(Field("rotation", JsonString(rotation)));
      var colors = new List<string>();
      if (lightColors != null)
        colors.Add(Field("light", Palette(lightColors)));
      if (darkColors != null)
        colors.Add(Field("dark", Palette(darkColors)));
      if (colors.Count > 0)
        fields.Add(Field("colors", JsonObject(colors.ToArray())));
      if (welcome != null)
        fields.Add(Field("welcome", JsonObject(Field("title", JsonString(welcome[0])), Field("text", JsonString(welcome[1])))));
      if (topics != null) {
        var items = new string[topics.Length];
        for (int i = 0; i < topics.Length; i++)
          items[i] = JsonObject(Field("label", JsonString(topics[i].Label)), Field("category", JsonString(topics[i].Category)),
                            Field("icon", JsonString(topics[i].Icon)));
        fields.Add(Field("topics", JsonObject(Field("title", JsonString(topicsTitle)), Field("items", "[" + string.Join(",", items) + "]"))));
      }
      if (contact != null)
        fields.Add(Field("contact", JsonObject(Field("title", JsonString(contact[0])), Field("text", JsonString(contact[1])))));
      return new Appearance(baseJson, JsonObject(fields.ToArray()));
    }

    private static string JsonString(string value) {
      var output = new StringBuilder(value.Length + 2);
      Cluefolk.Quote(output, value);
      return output.ToString();
    }

    private static string Field(string key, string json) { return JsonString(key) + ":" + json; }

    private static string JsonObject(params string[] fields) { return "{" + string.Join(",", fields) + "}"; }

    private static string Palette(string[] colors) {
      var fields = new string[ColorKeys.Length];
      for (int i = 0; i < ColorKeys.Length; i++)
        fields[i] = Field(ColorKeys[i], JsonString(colors[i]));
      return JsonObject(fields);
    }

    private static readonly string[] ColorKeys = { "accent", "action", "background", "surface", "text" };

    private static string[] Colors(string accent, string action, string background, string surface, string text) {
      var colors = new[] { accent, action, background, surface, text };
      for (int i = 0; i < colors.Length; i++)
        if (colors[i] == null)
          throw new ArgumentNullException(ColorKeys[i]);
      return colors;
    }
  }
}

[UnityEngine.Scripting.Preserve]
internal sealed class CluefolkDispatcher : MonoBehaviour {
  [UnityEngine.Scripting.Preserve]
  public void OnNativeMessage(string json) { Cluefolk.Receive(json); }
}
}
