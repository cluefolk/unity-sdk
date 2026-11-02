# Releasing

For maintainers. A release is a tag of this repository; Unity installs the package from the tag.

1. Release the Android SDK (`com.cluefolk:android-sdk`) and the iOS SDK of the same version first.
2. Set `version` in `package.json` and `Cluefolk.Version` in `Runtime/Cluefolk.cs` to that version; Android builds depend on `com.cluefolk:android-sdk:<Cluefolk.Version>`.
3. Replace the iOS framework from the iOS SDK checkout:

   ```sh
   Tools~/update-ios-framework.sh ../ios-sdk
   ```

4. Compile the C# for the Editor, Android and iOS with every installed Unity 2022.3+:

   ```sh
   python3 Tools~/verify-csharp.py
   ```

5. Build an Android and an iOS player from a test project that installs this package, and run them on devices.
6. Add the release to `CHANGELOG.md`, tag the commit with the version (`0.3.0`, no `v`), push the tag and create a GitHub release.
