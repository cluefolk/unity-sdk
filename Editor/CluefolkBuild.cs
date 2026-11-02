using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
#if UNITY_ANDROID
using UnityEditor.Android;
#endif
#if UNITY_IOS
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEditor.iOS.Xcode.Extensions;
#endif

namespace CluefolkSDK.Editor {
// Wires the native SDKs into player builds, so a game only adds this package:
// Android builds get com.cluefolk:android-sdk from Maven Central, iOS builds get
// the bundled Cluefolk.xcframework linked and embedded.

// The native SDKs need Android 7.0 (API 24) and iOS 15; say so before a long build fails.
sealed class CluefolkRequirements : IPreprocessBuildWithReport {
  public int callbackOrder => 0;

  public void OnPreprocessBuild(BuildReport report) {
    var target = report.summary.platform;
    if (target == BuildTarget.Android && (int)PlayerSettings.Android.minSdkVersion < 24)
      throw new BuildFailedException("Cluefolk needs Android 7.0 (API 24) or later. Set Player Settings → Other Settings → Minimum API Level to 24 or higher.");
    if (target == BuildTarget.iOS && (!float.TryParse(PlayerSettings.iOS.targetOSVersionString, NumberStyles.Float, CultureInfo.InvariantCulture, out var ios) || ios < 15))
      throw new BuildFailedException("Cluefolk needs iOS 15 or later. Set Player Settings → Other Settings → Target minimum iOS Version to 15.0 or higher.");
  }
}

#if UNITY_ANDROID
sealed class CluefolkAndroidBuild : IPostGenerateGradleAndroidProject {
  public int callbackOrder => 0;

  // path is the unityLibrary module of the generated Gradle project.
  public void OnPostGenerateGradleAndroidProject(string path) {
    var gradle = Path.Combine(path, "build.gradle");
    var text = File.ReadAllText(gradle);
    if (!text.Contains("com.cluefolk:android-sdk")) {
      var dependencies = new Regex(@"^dependencies\s*\{", RegexOptions.Multiline);
      if (!dependencies.IsMatch(text))
        throw new BuildFailedException("Cluefolk could not find the dependencies block in " + gradle);
      text = dependencies.Replace(text, m => m.Value + "\n    implementation 'com.cluefolk:android-sdk:" + Cluefolk.Version + "'", 1);
      File.WriteAllText(gradle, text);
    }
    // The SDK depends on AndroidX, which Unity 2022.3 does not switch on by itself.
    var properties = Path.Combine(Path.GetDirectoryName(path), "gradle.properties");
    var current = File.Exists(properties) ? File.ReadAllText(properties) : "";
    if (!Regex.IsMatch(current, @"^\s*android\.useAndroidX\s*=", RegexOptions.Multiline))
      File.AppendAllText(properties, "\nandroid.useAndroidX=true\n");
  }
}
#endif

#if UNITY_IOS
static class CluefolkIOSBuild {
  const string Framework = "Frameworks/com.cluefolk.sdk/Cluefolk.xcframework";

  [PostProcessBuild(100)]
  static void OnPostprocessBuild(BuildTarget target, string project) {
    if (target != BuildTarget.iOS) return;
    var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(CluefolkIOSBuild).Assembly);
    var source = Path.Combine(package.resolvedPath, "Native~", "Cluefolk.xcframework");
    var destination = Path.Combine(project, Framework);
    if (Directory.Exists(destination)) Directory.Delete(destination, true);
    Copy(source, destination);

    var pbxPath = PBXProject.GetPBXProjectPath(project);
    var pbx = new PBXProject();
    pbx.ReadFromFile(pbxPath);
    var file = pbx.FindFileGuidByProjectPath(Framework) ?? pbx.AddFile(Framework, Framework, PBXSourceTree.Source);
    // UnityFramework compiles the Objective-C++ bridge, so it links the SDK;
    // the app embeds it.
    var unityFramework = pbx.GetUnityFrameworkTargetGuid();
    pbx.AddFileToBuildSection(unityFramework, pbx.GetFrameworksBuildPhaseByTarget(unityFramework), file);
    pbx.AddFileToEmbedFrameworks(pbx.GetUnityMainTargetGuid(), file);
    pbx.WriteToFile(pbxPath);
  }

  static void Copy(string from, string to) {
    Directory.CreateDirectory(to);
    foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
    foreach (var directory in Directory.GetDirectories(from)) Copy(directory, Path.Combine(to, Path.GetFileName(directory)));
  }
}
#endif
}
