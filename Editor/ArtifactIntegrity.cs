using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CluefolkSDK.Editor {
internal static class ArtifactIntegrity {
  internal sealed class Manifest {
    internal string Version;
    internal string WidgetDigest;
    internal string TreeSha256;
  }

  private static readonly Regex VersionPattern = new Regex(
      @"\A[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?\z",
      RegexOptions.CultureInvariant);
  private static readonly Regex WidgetPattern = new Regex(
      @"\A[0-9a-f]{16}\z", RegexOptions.CultureInvariant);
  private static readonly Regex HashPattern = new Regex(
      @"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant);

  internal static Manifest Verify(string framework, string manifestPath,
                                  string expectedVersion) {
    if (!Directory.Exists(framework))
      throw new InvalidOperationException(
          "The bundled Cluefolk.xcframework is missing.");
    Manifest manifest = ReadManifest(manifestPath);
    if (!string.Equals(manifest.Version, expectedVersion,
                       StringComparison.Ordinal))
      throw new InvalidOperationException(
          "The bundled iOS SDK version " + manifest.Version +
          " does not match Unity SDK " + expectedVersion + ".");
    string actual = TreeSha256(framework);
    if (!string.Equals(actual, manifest.TreeSha256, StringComparison.Ordinal))
      throw new InvalidOperationException(
          "The bundled Cluefolk.xcframework checksum does not match its " +
          "integrity manifest. Run Tools~/update-ios-framework.sh from a " +
          "clean matching iOS SDK checkout.");
    return manifest;
  }

  internal static Manifest ReadManifest(string path) {
    if (!File.Exists(path))
      throw new InvalidOperationException(
          "The bundled iOS framework integrity manifest is missing.");
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (string raw in File.ReadAllLines(path)) {
      string line = raw.Trim();
      if (line.Length == 0)
        continue;
      int separator = line.IndexOf('=');
      if (separator <= 0 || separator == line.Length - 1)
        throw new InvalidOperationException(
            "The bundled iOS framework integrity manifest is invalid.");
      string key = line.Substring(0, separator);
      string value = line.Substring(separator + 1);
      if (!values.TryAdd(key, value))
        throw new InvalidOperationException(
            "The bundled iOS framework integrity manifest has duplicate keys.");
    }
    if (values.Count != 3 || !values.TryGetValue("version", out var version) ||
        !values.TryGetValue("widget", out var widget) ||
        !values.TryGetValue("sha256", out var sha256) ||
        !VersionPattern.IsMatch(version) || !WidgetPattern.IsMatch(widget) ||
        !HashPattern.IsMatch(sha256))
      throw new InvalidOperationException(
          "The bundled iOS framework integrity manifest is invalid.");
    return new Manifest {
      Version = version,
      WidgetDigest = widget,
      TreeSha256 = sha256,
    };
  }

  // Hash file content and relative names so metadata and checkout location do
  // not affect the digest. The release Python helper implements the same
  // format: path, NUL, lowercase file SHA-256, newline.
  internal static string TreeSha256(string directory) {
    string root = Path.GetFullPath(directory).TrimEnd(
        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var files = new List<string>(Directory.GetFiles(
        root, "*", SearchOption.AllDirectories));
    if (files.Count == 0)
      throw new InvalidOperationException(
          "The bundled Cluefolk.xcframework is empty.");
    files.Sort((left, right) => string.CompareOrdinal(
        Relative(root, left), Relative(root, right)));
    using (var aggregate = SHA256.Create()) {
      foreach (string file in files) {
        string relative = Relative(root, file);
        string fileHash;
        using (var input = File.OpenRead(file))
        using (var hash = SHA256.Create())
          fileHash = Hex(hash.ComputeHash(input));
        byte[] record = Encoding.UTF8.GetBytes(relative + "\0" + fileHash + "\n");
        aggregate.TransformBlock(record, 0, record.Length, record, 0);
      }
      aggregate.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
      return Hex(aggregate.Hash);
    }
  }

  private static string Relative(string root, string file) {
    return file.Substring(root.Length + 1).Replace(
        Path.DirectorySeparatorChar, '/');
  }

  private static string Hex(byte[] value) {
    var result = new StringBuilder(value.Length * 2);
    foreach (byte item in value)
      result.Append(item.ToString("x2"));
    return result.ToString();
  }
}
}
