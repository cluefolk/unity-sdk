# Releasing

For maintainers. A release is a tag of this repository; Unity installs the package from the tag.

1. Release the Android SDK (`com.cluefolk:android-sdk`) and the iOS SDK of the same version first.
2. Set `version` in `package.json` and `Cluefolk.Version` in `Runtime/Cluefolk.cs` to that version; Android builds depend on `com.cluefolk:android-sdk:<Cluefolk.Version>`.
3. From the clean, matching iOS SDK release tag, copy the audited widget digest
   printed at the top of `Sources/Cluefolk/WidgetAssets.swift` and replace the
   iOS framework. The updater refuses dirty or untagged sources and
   version/digest mismatches, validates both framework slices, and records a
   content checksum checked by Unity builds:

   ```sh
   Tools~/update-ios-framework.sh ../ios-sdk e3931edad88752a7
   ```

4. CI runs the pure hosted .NET security harness and XCFramework integrity
   checks with `Tools~/ci-security.sh`. That fallback compiles the real
   Runtime/Editor sources with separate Editor, Android-player and iOS-player
   defines against explicit test stubs. These are source compilation gates, not
   Unity runtime or device acceptance. Its integrity check only proves that the
   checked-in framework matches its manifest and the current package version;
   it does not prove unpublished native fixes have been propagated. Step 3
   performs that propagation from the released iOS tag. Before release, run
   the security regression tests and compile the C# for the Editor, Android and
   iOS with every installed Unity 2022.3+:

   ```sh
   python3 Tools~/test-security.py
   python3 Tools~/verify-csharp.py
   ```

5. Build an Android and an iOS player from a test project that installs this package, and run them on devices.
6. Add the release to `CHANGELOG.md`, tag the commit with the version (`0.3.0`, no `v`), push the tag and create a GitHub release.
