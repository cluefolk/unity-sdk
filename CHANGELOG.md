# Changelog

## 0.3.1 — 2026-11-02

- Uses Android SDK 0.3.1 from Maven Central and the iOS SDK 0.3.1 framework, with the updated support screen: replies signed with the operator's first name, compact delivery ticks, no player ID inside a conversation, and the "Talk to the team" button at the bottom of the home screen.

## 0.3.0 — 2026-11-02

First public release.

- Install from `https://github.com/cluefolk/unity-sdk.git#0.3.0`; no native files to copy by hand.
- Android builds add `com.cluefolk:android-sdk` 0.3.0 from Maven Central and switch AndroidX on.
- iOS builds link and embed the bundled `Cluefolk.xcframework`.
- Builds stop with a clear message below Android API 24 or iOS 15.
