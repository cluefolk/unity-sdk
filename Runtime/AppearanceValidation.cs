using System;
using System.Text;
using System.Text.RegularExpressions;

namespace CluefolkSDK {
// The native SDKs and the widget enforce these limits after parsing. Keep the
// cheap checks here too, before Unity builds large JSON strings or copies them
// through JNI/P/Invoke.
internal static class AppearanceValidation {
  internal const int MaxJsonLength = 1_500_000;
  internal const int MaxTitleLength = 100;
  internal const int MaxTextLength = 1000;
  internal const int MaxTopics = 12;
  internal const int MaxTopicLabelLength = 80;
  internal const int MaxTextIconLength = 32;
  internal const int MaxImageUrlLength = 700_040;
  internal const int MaxImageBytes = 525_000;
  internal const int MaxImageDimension = 2_048;
  internal const long MaxImagePixels = 4_194_304;

  private const string PngPrefix = "data:image/png;base64,";
  private const string JpegPrefix = "data:image/jpeg;base64,";
  private const string Base64Alphabet =
      "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
  private static readonly Regex HexColor = new Regex(
      @"\A#[0-9A-Fa-f]{6}\z", RegexOptions.CultureInvariant);
  private static readonly Regex CategoryKey = new Regex(
      @"\A[a-z][a-z0-9_]{0,49}\z", RegexOptions.CultureInvariant);

  internal static string BaseJson(string json) {
    if (json == null)
      return "";
    if (json.Length == 0)
      throw new ArgumentException(
          "The appearance JSON is empty; pass null for the default theme.",
          nameof(json));
    EnsureJsonLength(json, "The appearance JSON");
    ValidateBaseJsonFields(json);
    return json;
  }

  internal static void EnsureJsonLength(string json, string name) {
    if (json == null || json.Length > MaxJsonLength ||
        Encoding.UTF8.GetByteCount(json) > MaxJsonLength)
      throw new ArgumentException(name + " is larger than 1.5 MB.");
  }

  internal static void Title(string value) {
    if (string.IsNullOrWhiteSpace(value))
      throw new ArgumentException("The title must not be blank.", nameof(value));
    Limited(value, MaxTitleLength, "The title", nameof(value));
  }

  internal static void BlockTitle(string value, string block) {
    Limited(value, MaxTitleLength, "The " + block + " title", nameof(value));
  }

  internal static void BlockText(string value, string block) {
    Limited(value, MaxTextLength, "The " + block + " text", nameof(value));
  }

  internal static void Color(string value, string name) {
    if (value == null)
      throw new ArgumentNullException(name);
    if (!HexColor.IsMatch(value))
      throw new ArgumentException("The " + name + " color must be #RRGGBB.", name);
  }

  internal static void Topics(string title, AppearanceTopic[] items) {
    if (title == null)
      throw new ArgumentNullException(nameof(title));
    BlockTitle(title, "topics");
    if (items == null)
      throw new ArgumentNullException(nameof(items));
    if (items.Length > MaxTopics)
      throw new ArgumentException("At most 12 topics are allowed.", nameof(items));
    for (int index = 0; index < items.Length; index++) {
      if (items[index] == null)
        throw new ArgumentNullException(nameof(items));
      Topic(items[index].Label, items[index].Category, items[index].Icon,
            index + 1);
    }
  }

  internal static void Topic(string label, string category, string icon,
                             int number = 0) {
    string prefix = number > 0 ? "Topic " + number : "The topic";
    TopicLabel(label, prefix);
    TopicCategory(category, prefix);
    TopicIcon(icon, prefix);
  }

  private static void TopicLabel(string label, string prefix) {
    if (string.IsNullOrWhiteSpace(label))
      throw new ArgumentException(prefix + " label must not be blank.", nameof(label));
    Limited(label, MaxTopicLabelLength, prefix + " label", nameof(label));
  }

  private static void TopicCategory(string category, string prefix) {
    if (category == null)
      throw new ArgumentNullException(nameof(category));
    if (!CategoryKey.IsMatch(category))
      throw new ArgumentException(prefix + " category must be a category key.",
                                  nameof(category));
  }

  private static void TopicIcon(string icon, string prefix) {
    if (icon == null)
      throw new ArgumentNullException(nameof(icon));
    if (icon.StartsWith("data:", StringComparison.Ordinal)) {
      if (!ValidEmbeddedImage(icon))
        throw new ArgumentException(prefix +
            " icon must be a PNG or JPEG data URL up to 2048 x 2048.",
            nameof(icon));
    } else if (icon.Length > MaxTextIconLength || icon.IndexOf('<') >= 0 ||
               icon.IndexOf('>') >= 0) {
      throw new ArgumentException(prefix +
          " icon must be at most 32 characters without < or >.", nameof(icon));
    }
  }

  // Inspect security-relevant fields in a downloaded appearance before it is
  // passed to the native parser. This is deliberately a bounded JSON string
  // scanner, not a second full schema parser: native remains authoritative for
  // structure, required/unknown keys, modes, palettes and duplicate blocks.
  // Decoding every string means escaped keys and data-URL prefixes cannot hide
  // from the early limits.
  private static void ValidateBaseJsonFields(string json) {
    int index = 0;
    int labels = 0;
    int categories = 0;
    int icons = 0;
    int blocks = 0;
    while (index < json.Length) {
      if (json[index] != '"') {
        index++;
        continue;
      }
      string key = ReadJsonString(json, ref index);
      int cursor = SkipWhitespace(json, index);
      if (cursor >= json.Length || json[cursor] != ':')
        continue;
      if (key != "title" && key != "text" && key != "label" &&
          key != "category" && key != "icon" && key != "type")
        continue;
      cursor = SkipWhitespace(json, cursor + 1);
      if (cursor >= json.Length || json[cursor] != '"')
        throw new ArgumentException("Appearance field \"" + key +
                                    "\" must be a string.");
      string value = ReadJsonString(json, ref cursor);
      switch (key) {
      case "title":
        Limited(value, MaxTitleLength, "The appearance title", "json");
        break;
      case "text":
        Limited(value, MaxTextLength, "The appearance text", "json");
        break;
      case "label":
        if (++labels > MaxTopics)
          throw new ArgumentException("At most 12 topics are allowed.");
        TopicLabel(value, "The topic");
        break;
      case "category":
        if (++categories > MaxTopics)
          throw new ArgumentException("At most 12 topics are allowed.");
        TopicCategory(value, "The topic");
        break;
      case "icon":
        if (++icons > MaxTopics)
          throw new ArgumentException("At most 12 topics are allowed.");
        TopicIcon(value, "The topic");
        break;
      case "type":
        if (++blocks > 4)
          throw new ArgumentException("At most 4 appearance blocks are allowed.");
        break;
      }
      index = cursor;
    }
  }

  private static int SkipWhitespace(string json, int index) {
    while (index < json.Length && (json[index] == ' ' || json[index] == '\t' ||
           json[index] == '\n' || json[index] == '\r'))
      index++;
    return index;
  }

  private static string ReadJsonString(string json, ref int index) {
    if (index >= json.Length || json[index] != '"')
      throw new ArgumentException("Invalid appearance JSON string.");
    var result = new StringBuilder();
    index++;
    while (index < json.Length) {
      char character = json[index++];
      if (character == '"')
        return result.ToString();
      if (character < 32)
        throw new ArgumentException("Invalid control character in appearance JSON.");
      if (character != '\\') {
        result.Append(character);
        continue;
      }
      if (index >= json.Length)
        throw new ArgumentException("Invalid escape in appearance JSON.");
      char escaped = json[index++];
      switch (escaped) {
      case '"': result.Append('"'); break;
      case '\\': result.Append('\\'); break;
      case '/': result.Append('/'); break;
      case 'b': result.Append('\b'); break;
      case 'f': result.Append('\f'); break;
      case 'n': result.Append('\n'); break;
      case 'r': result.Append('\r'); break;
      case 't': result.Append('\t'); break;
      case 'u':
        if (index + 4 > json.Length)
          throw new ArgumentException("Invalid Unicode escape in appearance JSON.");
        int code = 0;
        for (int digit = 0; digit < 4; digit++) {
          int value = HexDigit(json[index++]);
          if (value < 0)
            throw new ArgumentException("Invalid Unicode escape in appearance JSON.");
          code = code * 16 + value;
        }
        result.Append((char)code);
        break;
      default:
        throw new ArgumentException("Invalid escape in appearance JSON.");
      }
    }
    throw new ArgumentException("Unterminated string in appearance JSON.");
  }

  private static int HexDigit(char value) {
    if (value >= '0' && value <= '9')
      return value - '0';
    if (value >= 'a' && value <= 'f')
      return value - 'a' + 10;
    if (value >= 'A' && value <= 'F')
      return value - 'A' + 10;
    return -1;
  }

  internal static void EnsureSerializedLength(long length) {
    if (length > MaxJsonLength)
      throw new ArgumentException("The appearance overrides are larger than 1.5 MB.");
  }

  // Length of Cluefolk.Quote(value), without allocating the quoted string.
  internal static int JsonStringLength(string value) {
    long length = 2;
    for (int index = 0; index < value.Length; index++) {
      char character = value[index];
      if (character == '"' || character == '\\' || character == '\b' ||
          character == '\f' || character == '\n' || character == '\r' ||
          character == '\t') {
        length += 2;
      } else if (character < 32 ||
                 char.IsHighSurrogate(character) &&
                     (index + 1 >= value.Length ||
                      !char.IsLowSurrogate(value[index + 1])) ||
                 char.IsLowSurrogate(character) &&
                     (index == 0 || !char.IsHighSurrogate(value[index - 1]))) {
        length += 6;
      } else {
        length++;
      }
      if (length > int.MaxValue)
        throw new ArgumentException("The appearance string is too large.");
    }
    return (int)length;
  }

  // UTF-8 byte count of Cluefolk.Quote(value), without allocating either the
  // quoted string or an encoded byte array.
  internal static int JsonStringUtf8Length(string value) {
    long length = 2;
    for (int index = 0; index < value.Length; index++) {
      char character = value[index];
      if (character == '"' || character == '\\' || character == '\b' ||
          character == '\f' || character == '\n' || character == '\r' ||
          character == '\t') {
        length += 2;
      } else if (character < 32 ||
                 char.IsHighSurrogate(character) &&
                     (index + 1 >= value.Length ||
                      !char.IsLowSurrogate(value[index + 1])) ||
                 char.IsLowSurrogate(character) &&
                     (index == 0 || !char.IsHighSurrogate(value[index - 1]))) {
        length += 6;
      } else if (character < 0x80) {
        length++;
      } else if (character < 0x800) {
        length += 2;
      } else if (char.IsHighSurrogate(character)) {
        length += 4;
        index++;
      } else {
        length += 3;
      }
      if (length > int.MaxValue)
        throw new ArgumentException("The appearance string is too large.");
    }
    return (int)length;
  }

  internal static bool ValidEmbeddedImage(string value) {
    if (value == null || value.Length > MaxImageUrlLength)
      return false;
    string encoded;
    bool png;
    if (value.StartsWith(PngPrefix, StringComparison.Ordinal)) {
      encoded = value.Substring(PngPrefix.Length);
      png = true;
    } else if (value.StartsWith(JpegPrefix, StringComparison.Ordinal)) {
      encoded = value.Substring(JpegPrefix.Length);
      png = false;
    } else {
      return false;
    }
    byte[] bytes = DecodeBase64(encoded);
    if (bytes == null || bytes.Length == 0 || bytes.Length > MaxImageBytes)
      return false;

    long width = 0;
    long height = 0;
    if (png && bytes.Length >= 24 &&
        bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4e &&
        bytes[3] == 0x47 && bytes[4] == 0x0d && bytes[5] == 0x0a &&
        bytes[6] == 0x1a && bytes[7] == 0x0a && UInt32(bytes, 8) == 13 &&
        bytes[12] == 0x49 && bytes[13] == 0x48 && bytes[14] == 0x44 &&
        bytes[15] == 0x52) {
      width = UInt32(bytes, 16);
      height = UInt32(bytes, 20);
    } else if (!png && bytes.Length >= 2 && bytes[0] == 0xff &&
               bytes[1] == 0xd8) {
      int offset = 2;
      while (offset + 1 < bytes.Length) {
        if (bytes[offset++] != 0xff)
          break;
        while (offset < bytes.Length && bytes[offset] == 0xff)
          offset++;
        if (offset >= bytes.Length)
          break;
        int marker = bytes[offset++];
        if (marker == 0xda || marker == 0xd9)
          break;
        if (marker == 0x01 || marker >= 0xd0 && marker <= 0xd7)
          continue;
        if (offset + 2 > bytes.Length)
          break;
        int segmentLength = UInt16(bytes, offset);
        if (segmentLength < 2 || offset + segmentLength > bytes.Length)
          break;
        if (marker >= 0xc0 && marker <= 0xcf && marker != 0xc4 &&
            marker != 0xc8 && marker != 0xcc && segmentLength >= 8) {
          height = UInt16(bytes, offset + 3);
          width = UInt16(bytes, offset + 5);
          break;
        }
        offset += segmentLength;
      }
    }
    return width >= 1 && height >= 1 && width <= MaxImageDimension &&
           height <= MaxImageDimension && width * height <= MaxImagePixels;
  }

  private static void Limited(string value, int maximum, string description,
                              string argument) {
    if (value == null)
      throw new ArgumentNullException(argument);
    if (value.Length > maximum)
      throw new ArgumentException(description + " is longer than " + maximum +
                                  " characters.", argument);
  }

  // Decodes the standard alphabet after bounding it, and rejects non-canonical
  // trailing bits. Padding is optional, as in the widget/native validators.
  private static byte[] DecodeBase64(string value) {
    if (value.Length == 0 || value.Length % 4 == 1)
      return null;
    int padding = 0;
    while (padding < value.Length && value[value.Length - padding - 1] == '=')
      padding++;
    if (padding > 2 || padding > 0 && value.Length % 4 != 0)
      return null;
    int content = value.Length - padding;
    if (padding == 1 && content % 4 != 3 ||
        padding == 2 && content % 4 != 2)
      return null;
    int outputLength = content * 6 / 8;
    if (outputLength > MaxImageBytes)
      return null;
    var output = new byte[outputLength];
    int buffer = 0;
    int bits = 0;
    int outputIndex = 0;
    for (int index = 0; index < content; index++) {
      int digit = Base64Alphabet.IndexOf(value[index]);
      if (digit < 0)
        return null;
      buffer = buffer << 6 | digit;
      bits += 6;
      if (bits >= 8) {
        bits -= 8;
        output[outputIndex++] = (byte)(buffer >> bits);
        buffer = bits == 0 ? 0 : buffer & ((1 << bits) - 1);
      }
    }
    return buffer == 0 && outputIndex == output.Length ? output : null;
  }

  private static int UInt16(byte[] bytes, int offset) {
    return bytes[offset] * 256 + bytes[offset + 1];
  }

  private static long UInt32(byte[] bytes, int offset) {
    return (long)bytes[offset] << 24 | (long)bytes[offset + 1] << 16 |
           (long)bytes[offset + 2] << 8 | bytes[offset + 3];
  }
}
}
