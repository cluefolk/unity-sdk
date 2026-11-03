#if CLUEFOLK_HOSTED_VALIDATION
#if UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
#error Hosted validation must not combine UNITY_EDITOR with a player platform.
#endif
#if UNITY_ANDROID && UNITY_IOS
#error Hosted validation must select only one player platform.
#endif
#if !UNITY_EDITOR && !UNITY_ANDROID && !UNITY_IOS
#error Hosted validation must select EDITOR, ANDROID, or IOS.
#endif
#endif

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CluefolkSDK;
using CluefolkSDK.Editor;

internal static class SecurityTests {
  private static int checks;

  private static void Check(bool condition, string description) {
    checks++;
    if (!condition)
      throw new Exception("Check failed: " + description);
  }

  private static void Throws<T>(Action action, string description)
      where T : Exception {
    checks++;
    try {
      action();
    } catch (T) {
      return;
    }
    throw new Exception("Expected " + typeof(T).Name + ": " + description);
  }

  private static async Task ThrowsAsync<T>(Func<Task> action,
                                            string description)
      where T : Exception {
    checks++;
    try {
      await action();
    } catch (T) {
      return;
    }
    throw new Exception("Expected " + typeof(T).Name + ": " + description);
  }

  private static string Png(int width, int height, string media = "png",
                            int byteCount = 24, int ihdrLength = 13) {
    var bytes = new byte[Math.Max(24, byteCount)];
    byte[] signature = { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
    Array.Copy(signature, bytes, signature.Length);
    PutUInt32(bytes, 8, ihdrLength);
    bytes[12] = 0x49; bytes[13] = 0x48; bytes[14] = 0x44; bytes[15] = 0x52;
    PutUInt32(bytes, 16, width);
    PutUInt32(bytes, 20, height);
    return "data:image/" + media + ";base64," + Convert.ToBase64String(bytes);
  }

  private static string Jpeg(int width, int height) {
    var bytes = new byte[21];
    bytes[0] = 0xff; bytes[1] = 0xd8;
    bytes[2] = 0xff; bytes[3] = 0xc0;
    bytes[4] = 0; bytes[5] = 17; bytes[6] = 8;
    bytes[7] = (byte)(height >> 8); bytes[8] = (byte)height;
    bytes[9] = (byte)(width >> 8); bytes[10] = (byte)width;
    return "data:image/jpeg;base64," + Convert.ToBase64String(bytes);
  }

  private static void PutUInt32(byte[] bytes, int offset, int value) {
    bytes[offset] = (byte)(value >> 24);
    bytes[offset + 1] = (byte)(value >> 16);
    bytes[offset + 2] = (byte)(value >> 8);
    bytes[offset + 3] = (byte)value;
  }

  private static void Appearance() {
    AppearanceValidation.Title(new string('a', 100));
    Throws<ArgumentException>(() => AppearanceValidation.Title("  \t\n"),
                              "blank title");
    Throws<ArgumentException>(
        () => AppearanceValidation.Title(new string('a', 101)),
        "oversized title");
    AppearanceValidation.BlockText(new string('a', 1000), "welcome");
    Throws<ArgumentException>(
        () => AppearanceValidation.BlockText(new string('a', 1001), "welcome"),
        "oversized block text");
    AppearanceValidation.Color("#aB09fF", "accent");
    Throws<ArgumentException>(() => AppearanceValidation.Color("red", "accent"),
                              "non-hex color");
    AppearanceValidation.Topic("Account", "account_2", "👤");
    Throws<ArgumentException>(
        () => AppearanceValidation.Topic("Account", "Account", "👤"),
        "invalid category");
    Throws<ArgumentException>(
        () => AppearanceValidation.Topic("Account", "account\n", "👤"),
        "category trailing newline");
    Throws<ArgumentException>(
        () => AppearanceValidation.Topic("Account", "account", "<svg>"),
        "HTML-like text icon");
    Check(AppearanceValidation.ValidEmbeddedImage(Png(1, 1)), "small PNG");
    Check(AppearanceValidation.ValidEmbeddedImage(Png(2048, 2048)),
          "maximum PNG dimensions");
    Check(!AppearanceValidation.ValidEmbeddedImage(Png(2049, 1)),
          "oversized PNG dimension");
    Check(!AppearanceValidation.ValidEmbeddedImage(Png(1, 1, "png", 24, 12)),
          "invalid PNG IHDR length");
    Check(!AppearanceValidation.ValidEmbeddedImage(Png(1, 1, "jpeg")),
          "media type/signature mismatch");
    Check(AppearanceValidation.ValidEmbeddedImage(Jpeg(640, 480)),
          "bounded JPEG SOF");
    Check(!AppearanceValidation.ValidEmbeddedImage(Jpeg(4096, 1)),
          "oversized JPEG dimension");
    Throws<ArgumentException>(
        () => AppearanceValidation.BaseJson(new string('x', 1_500_001)),
        "oversized base appearance JSON");
    Throws<ArgumentException>(
        () => AppearanceValidation.BaseJson(new string('é', 750_001)),
        "base appearance JSON exceeds UTF-8 byte limit");
    Throws<ArgumentException>(() => AppearanceValidation.BaseJson(""),
                              "empty base appearance JSON");
    string escapedBomb = Png(4096, 1).Replace(":", "\\u003a");
    Throws<ArgumentException>(
        () => AppearanceValidation.BaseJson(
            "{\"blocks\":[{\"type\":\"topics\",\"items\":[{" +
            "\"label\":\"Account\",\"category\":\"account\"," +
            "\"\\u0069con\":\"" + escapedBomb + "\"}]}]}"),
        "escaped data-image bomb in base appearance JSON");
    var excessiveTopics = new StringBuilder("{\"items\":[");
    for (int index = 0; index < 13; index++) {
      if (index > 0)
        excessiveTopics.Append(',');
      excessiveTopics.Append("{\"label\":\"Topic\"}");
    }
    excessiveTopics.Append("]}");
    Throws<ArgumentException>(
        () => AppearanceValidation.BaseJson(excessiveTopics.ToString()),
        "topic count in base appearance JSON");
    Check(AppearanceValidation.JsonStringLength("\b\f\n\"\\") == 12,
          "JSON escaped length");
    Check(AppearanceValidation.JsonStringLength("\ud800") == 8,
          "lone surrogate escaped length");
    Check(AppearanceValidation.JsonStringUtf8Length("é") == 4,
          "UTF-8 JSON string length");
    Check(AppearanceValidation.JsonStringUtf8Length("😀") == 6,
          "UTF-8 surrogate-pair JSON string length");

    var built = new Appearance.Builder()
        .Title("Support")
        .Topics("Choose", new AppearanceTopic("Account", "account", Png(1, 1)))
        .Build();
    Check(built.Overrides.Contains("\"category\":\"account\""),
          "public builder serializes a validated topic");
    var tooMany = new AppearanceTopic[13];
    for (int index = 0; index < tooMany.Length; index++)
      tooMany[index] = new AppearanceTopic("Topic", "account", "T");
    Throws<ArgumentException>(() => new Appearance.Builder().Topics("Topics", tooMany),
                              "public builder topic count");
    string largeIcon = Png(1, 1, "png", 200_000);
    var largeTopics = new AppearanceTopic[6];
    for (int index = 0; index < largeTopics.Length; index++)
      largeTopics[index] = new AppearanceTopic("Topic", "account", largeIcon);
    Throws<ArgumentException>(
        () => new Appearance.Builder().Topics("Topics", largeTopics).Build(),
        "aggregate override size before serialization");
  }

  private static void Gradle() {
    const string empty = "// com.cluefolk:android-sdk:9.9.9\ndependencies {\n}\n";
    string injected = GradleDependency.EnsureExact(empty, "0.3.1");
    Check(injected.Contains("implementation 'com.cluefolk:android-sdk:0.3.1'"),
          "comment does not suppress injection");
    string exact = "dependencies {\n  implementation(\"com.cluefolk:android-sdk:0.3.1\")\n}";
    Check(GradleDependency.EnsureExact(exact, "0.3.1") == exact,
          "exact dependency remains unchanged");
    Throws<InvalidOperationException>(
        () => GradleDependency.EnsureExact(
            "dependencies {\n implementation 'com.cluefolk:android-sdk:0.3.0'\n}",
            "0.3.1"), "stale dependency");
    Throws<InvalidOperationException>(
        () => GradleDependency.EnsureExact(
            "dependencies {\n implementation \"com.cluefolk:android-sdk:$sdkVersion\"\n}",
            "0.3.1"), "dynamic dependency");
    Throws<InvalidOperationException>(
        () => GradleDependency.EnsureExact(
            "dependencies {\n implementation 'com.cluefolk:android-sdk:0.3.1'\n" +
            " implementation \"com.cluefolk:android-sdk:$sdkVersion\"\n}",
            "0.3.1"), "exact plus dynamic dependency");
    Throws<InvalidOperationException>(
        () => GradleDependency.EnsureExact(
            "/* dependencies { implementation 'com.cluefolk:android-sdk:0.3.1' } */",
            "0.3.1"), "commented dependencies block");
  }

  private static void Integrity() {
    string temporary = Path.Combine(Path.GetTempPath(),
        "cluefolk-integrity-" + Guid.NewGuid().ToString("N"));
    string framework = Path.Combine(temporary, "Cluefolk.xcframework");
    string manifest = framework + ".integrity";
    try {
      Directory.CreateDirectory(Path.Combine(framework, "slice"));
      File.WriteAllText(Path.Combine(framework, "b"), "second");
      File.WriteAllText(Path.Combine(framework, "slice", "a"), "first");
      string hash = ArtifactIntegrity.TreeSha256(framework);
      Check(hash == "9956f6c45d6a5835f1f04f8059bd259358f3c908b8fd7461f65eeb0969742ffc",
            "C# and release-tool tree hash format");
      File.WriteAllText(manifest, "version=0.3.1\nwidget=0123456789abcdef\n" +
                                  "sha256=" + hash + "\n");
      Check(ArtifactIntegrity.Verify(framework, manifest, "0.3.1")
                .WidgetDigest == "0123456789abcdef",
            "valid artifact manifest");
      File.AppendAllText(Path.Combine(framework, "b"), "tampered");
      Throws<InvalidOperationException>(
          () => ArtifactIntegrity.Verify(framework, manifest, "0.3.1"),
          "tampered framework");
      Throws<InvalidOperationException>(
          () => ArtifactIntegrity.Verify(framework, manifest, "0.4.0"),
          "mismatched framework version");
    } finally {
      if (Directory.Exists(temporary))
        Directory.Delete(temporary, true);
    }
  }

  private static async Task Identity() {
    string token = await IdentityProviderRunner.Run(
        () => Task.FromResult("token"), CancellationToken.None, 100);
    Check(token == "token", "identity provider result");
    var late = new TaskCompletionSource<string>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    await ThrowsAsync<TimeoutException>(
        () => IdentityProviderRunner.Run(() => late.Task,
                                         CancellationToken.None, 10),
        "identity provider timeout");
    late.TrySetException(new InvalidOperationException("late"));
    using (var cancellation = new CancellationTokenSource()) {
      var pending = new TaskCompletionSource<string>(
          TaskCreationOptions.RunContinuationsAsynchronously);
      Task<string> run = IdentityProviderRunner.Run(
          () => pending.Task, cancellation.Token, 1000);
      cancellation.Cancel();
      await ThrowsAsync<OperationCanceledException>(async () => await run,
                                                     "identity cancellation");
      pending.TrySetResult("ignored");
    }
  }

  private static async Task<int> Main(string[] arguments) {
    Appearance();
    Gradle();
    Integrity();
    if (arguments.Length == 3)
      ArtifactIntegrity.Verify(arguments[0], arguments[1], arguments[2]);
    await Identity();
    Console.WriteLine("Security validation: " + checks + " checks passed");
    return 0;
  }
}

// The tests compile the real runtime source without loading Unity native
// assemblies. Only these declarations are needed by code paths not exercised
// in the pure validation harness.
namespace UnityEngine {
public class Object {
  public static void DontDestroyOnLoad(Object value) {}
  public static void Destroy(Object value) {}
}
public class GameObject : Object {
  public string name;
  public GameObject(string value) { name = value; }
  public T AddComponent<T>() { return default(T); }
}
public class MonoBehaviour : Object {}
public sealed class AndroidJavaClass : IDisposable {
  public AndroidJavaClass(string name) {}
  public void CallStatic(string method, params object[] arguments) {}
  public T CallStatic<T>(string method, params object[] arguments) {
    return default(T);
  }
  public void Dispose() {}
}
public static class JsonUtility {
  public static T FromJson<T>(string json) { return default(T); }
}
}
namespace UnityEngine.Scripting {
[AttributeUsage(AttributeTargets.All)]
public sealed class PreserveAttribute : Attribute {}
}

// The hosted .NET project also compiles the exact Editor sources. These stubs
// intentionally model only the signatures that source references; they do not
// make the hosted build a Unity Editor compilation.
namespace UnityEditor {
public enum BuildTarget { Android, iOS }
public enum AndroidSdkVersions { AndroidApiLevel24 = 24 }
public static class PlayerSettings {
  public static class Android {
    public static AndroidSdkVersions minSdkVersion;
  }
  public static class iOS {
    public static string targetOSVersionString;
  }
}
}
namespace UnityEditor.Build {
public interface IPreprocessBuildWithReport {
  int callbackOrder { get; }
  void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report);
}
public sealed class BuildFailedException : Exception {
  public BuildFailedException(string message) : base(message) {}
}
}
namespace UnityEditor.Build.Reporting {
public sealed class BuildReport {
  public BuildSummary summary;
}
public struct BuildSummary {
  public UnityEditor.BuildTarget platform;
}
}
namespace UnityEditor.Android {
public interface IPostGenerateGradleAndroidProject {
  int callbackOrder { get; }
  void OnPostGenerateGradleAndroidProject(string path);
}
}
namespace UnityEditor.Callbacks {
[AttributeUsage(AttributeTargets.Method)]
public sealed class PostProcessBuildAttribute : Attribute {
  public PostProcessBuildAttribute(int callbackOrder) {}
}
}
namespace UnityEditor.PackageManager {
public sealed class PackageInfo {
  public string resolvedPath;
  public static PackageInfo FindForAssembly(
      System.Reflection.Assembly assembly) { return new PackageInfo(); }
}
}
namespace UnityEditor.iOS.Xcode {
public enum PBXSourceTree { Source }
public sealed class PBXProject {
  public static string GetPBXProjectPath(string project) { return ""; }
  public void ReadFromFile(string path) {}
  public string FindFileGuidByProjectPath(string path) { return null; }
  public string AddFile(string path, string projectPath,
                        PBXSourceTree sourceTree) { return ""; }
  public string GetUnityFrameworkTargetGuid() { return ""; }
  public string GetFrameworksBuildPhaseByTarget(string target) { return ""; }
  public void AddFileToBuildSection(string target, string phase, string file) {}
  public string GetUnityMainTargetGuid() { return ""; }
  public void WriteToFile(string path) {}
}
}
namespace UnityEditor.iOS.Xcode.Extensions {
public static class PBXProjectExtensions {
  public static void AddFileToEmbedFrameworks(
      this UnityEditor.iOS.Xcode.PBXProject project, string target,
      string file) {}
}
}
