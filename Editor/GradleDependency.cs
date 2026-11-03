using System;
using System.Text;
using System.Text.RegularExpressions;

namespace CluefolkSDK.Editor {
internal static class GradleDependency {
  private const string Module = "com.cluefolk:android-sdk";
  private static readonly Regex ExactDependency = new Regex(
      @"^\s*implementation\s*(?:\(\s*)?(['""])(com\.cluefolk:android-sdk:([^'""\r\n]+))\1\s*\)?\s*$",
      RegexOptions.Multiline | RegexOptions.CultureInvariant);
  private static readonly Regex DependenciesBlock = new Regex(
      @"^\s*dependencies\s*\{", RegexOptions.Multiline |
      RegexOptions.CultureInvariant);
  private static readonly Regex SafeVersion = new Regex(
      @"\A[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?\z",
      RegexOptions.CultureInvariant);

  internal static string EnsureExact(string text, string expectedVersion) {
    if (text == null)
      throw new ArgumentNullException(nameof(text));
    if (string.IsNullOrEmpty(expectedVersion) ||
        !SafeVersion.IsMatch(expectedVersion))
      throw new InvalidOperationException("Cluefolk has an invalid native SDK version.");

    string active = StripComments(text);
    MatchCollection matches = ExactDependency.Matches(active);
    if (matches.Count > 1)
      throw new InvalidOperationException(
          "More than one active Cluefolk Android SDK dependency was found.");
    if (matches.Count == 1) {
      string remainder = active.Remove(matches[0].Index, matches[0].Length);
      if (remainder.IndexOf(Module, StringComparison.Ordinal) >= 0)
        throw new InvalidOperationException(
            "More than one active Cluefolk Android SDK reference was found.");
      string foundVersion = matches[0].Groups[3].Value;
      if (!string.Equals(foundVersion, expectedVersion,
                         StringComparison.Ordinal))
        throw new InvalidOperationException(
            "Cluefolk Android SDK dependency must be exactly " + Module + ":" +
            expectedVersion + ", but found " + matches[0].Groups[2].Value + ".");
      return text;
    }
    if (active.IndexOf(Module, StringComparison.Ordinal) >= 0)
      throw new InvalidOperationException(
          "An active Cluefolk Android SDK reference uses an unsupported or " +
          "dynamic Gradle declaration. Use implementation '" + Module + ":" +
          expectedVersion + "'.");

    Match dependencies = DependenciesBlock.Match(active);
    if (!dependencies.Success)
      throw new InvalidOperationException(
          "Cluefolk could not find an active dependencies block.");
    int brace = active.IndexOf('{', dependencies.Index,
                               dependencies.Length);
    if (brace < 0)
      throw new InvalidOperationException(
          "Cluefolk could not find the dependencies block opening brace.");
    return text.Insert(brace + 1, "\n    implementation '" + Module + ":" +
                                      expectedVersion + "'");
  }

  // Replace comments with spaces while preserving newlines and offsets, and
  // leave string literals intact so dependency coordinates can be inspected.
  internal static string StripComments(string text) {
    var output = new StringBuilder(text.Length);
    bool lineComment = false;
    bool blockComment = false;
    char quote = '\0';
    bool escaped = false;
    for (int index = 0; index < text.Length; index++) {
      char current = text[index];
      char next = index + 1 < text.Length ? text[index + 1] : '\0';
      if (lineComment) {
        if (current == '\n' || current == '\r') {
          lineComment = false;
          output.Append(current);
        } else {
          output.Append(' ');
        }
        continue;
      }
      if (blockComment) {
        if (current == '*' && next == '/') {
          output.Append("  ");
          index++;
          blockComment = false;
        } else {
          output.Append(current == '\n' || current == '\r' ? current : ' ');
        }
        continue;
      }
      if (quote != '\0') {
        output.Append(current);
        if (escaped) {
          escaped = false;
        } else if (current == '\\') {
          escaped = true;
        } else if (current == quote) {
          quote = '\0';
        }
        continue;
      }
      if (current == '\'' || current == '"') {
        quote = current;
        output.Append(current);
      } else if (current == '/' && next == '/') {
        output.Append("  ");
        index++;
        lineComment = true;
      } else if (current == '/' && next == '*') {
        output.Append("  ");
        index++;
        blockComment = true;
      } else {
        output.Append(current);
      }
    }
    return output.ToString();
  }
}
}
