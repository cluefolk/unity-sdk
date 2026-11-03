# Changelog

## 0.3.2 — 2026-11-03

- Use Android SDK 0.3.2 and the iOS SDK 0.3.2 framework, including bounded image handling, strict bridge validation and the iOS privacy manifest.
- Reject oversized or unsafe appearance values and embedded PNG/JPEG icons in C# before JSON serialization or native interop.
- Fail Android builds when an existing Cluefolk dependency is stale, dynamic or ambiguous instead of accepting a substring match.
- Verify clean, version-matched iOS SDK sources, the expected widget digest and a checked-in XCFramework content checksum during release/build workflows.
- Bound identity provider requests to the native 15-second timeout and cancel stale requests when the provider or game account changes.
- Compile Editor, Android-player and iOS-player sources separately in hosted CI and run security and artifact integrity checks.

## 0.3.1 — 2026-11-02

- Uses Android SDK 0.3.1 from Maven Central and the iOS SDK 0.3.1 framework, with the updated support screen: replies signed with the operator's first name, compact delivery ticks, no player ID inside a conversation, and the "Talk to the team" button at the bottom of the home screen.

## 0.3.0 — 2026-11-02

First public release.

- Install from `https://github.com/cluefolk/unity-sdk.git#0.3.0`; no native files to copy by hand.
- Android builds add `com.cluefolk:android-sdk` 0.3.0 from Maven Central and switch AndroidX on.
- iOS builds link and embed the bundled `Cluefolk.xcframework`.
- Builds stop with a clear message below Android API 24 or iOS 15.
