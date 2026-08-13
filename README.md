# MV2 RAM Launcher for Chrome

`mv2ctl` finds Chrome's Manifest V2 deprecation gates and applies verified in-memory edits after `chrome.dll` is loaded. It does not modify `chrome.dll` on disk.

If Chrome previously persisted the MV2-only disable reason (`8388608`), `launch` removes only that reason before startup, preserves unrelated disable reasons, and restamps Chrome's protected preferences. It creates a one-time `Secure Preferences.mv2ctl.bak` backup.

The current semantic rule was developed and tested against Google Chrome `151.0.7922.137` and `151.0.7922.138` x64. Other builds are accepted only when exactly one candidate passes every semantic check.

Vietnamese instructions: [README.vi.md](README.vi.md)

## Packaged executable

The recommended self-contained WinUI 3 GUI is:

```powershell
.\artifacts\win-x64-gui\ChromeMv2Launcher.exe
```

It analyzes Chrome on startup, monitors whether Chrome is running, repairs persisted MV2 profile state when necessary, and exposes the verified RAM launch as one primary button. GitHub Releases use a normal ZIP containing the self-contained app rather than a self-extracting executable, reducing opaque packaging and making every runtime file inspectable.

The GUI supports Vietnamese, English, and Simplified Chinese. It selects the Windows UI language on first run and persists the user's choice under `%LOCALAPPDATA%\ChromeMv2Launcher`.

### CLI

The self-contained Windows x64 build is at `artifacts\win-x64-self-contained\mv2ctl.exe`. It does not require a separately installed .NET runtime.

```powershell
.\artifacts\win-x64-self-contained\mv2ctl.exe analyze
.\artifacts\win-x64-self-contained\mv2ctl.exe launch
```

## Safety properties

- Scans only the executable `.text` PE section.
- Does not use an expected file offset or RVA.
- Enumerates every byte-pattern candidate and applies structural checks.
- Requires exactly one semantic match; zero or multiple matches abort.
- Locates both impact-checker overloads and two compiler-generated management clones.
- Locates and neutralizes the startup branch whose verified target constructs disable reason `0x800000`.
- Verifies every original byte in the remote process before writing any target.
- Reads every byte back after writing and flushes the instruction cache.
- Never writes to `chrome.dll` on disk.
- Refuses to launch while any Chrome process is already running.

## Build and test

```powershell
dotnet build .\ManifestV2.slnx -c Release
dotnet test .\ManifestV2.slnx -c Release
dotnet run --project .\src\Mv2Enabler -c Release -- analyze
dotnet run --project .\src\Mv2Enabler -c Release -- smoke-test
dotnet run --project .\src\Mv2Enabler -c Release -- functional-ui-test --extension .\test-extension
```

The patch engine lives in `src\Mv2Enabler.Core`; both the CLI and WinUI 3 app reference the same implementation.

The automated smoke test creates a uniquely named profile below `%TEMP%\mv2ctl-smoke`, launches headless Chrome, applies and verifies the RAM patch, confirms Chrome remains alive, stops only the process tree it created, and removes that temporary profile.

The functional UI test uses Chrome's normal **Load unpacked** workflow, selects `test-extension`, and requires a DevTools target whose `chrome-extension://` host exactly matches the ID derived from the test manifest's fixed public key. It also checks the extension manager reports the extension as enabled without the `unsupportedManifestVersion` disable reason. Its profile is uniquely named and removed afterward. The test briefly opens a Chrome window and restores the clipboard text it temporarily uses for the native folder picker.

## Launch Chrome

Close every Chrome window and background process, then run:

```powershell
dotnet run --project .\src\Mv2Enabler -c Release -- launch
```

Chrome arguments can be passed after `--`:

```powershell
dotnet run --project .\src\Mv2Enabler -c Release -- launch -- --user-data-dir=E:\Chrome-MV2-Profile
```

Every new browser session must start through `mv2ctl`; the in-memory change disappears when Chrome exits. An MV2 extension only needs to be loaded once: later patched launches preserve the installed entry and prevent Chrome from persisting the unsupported-manifest disable reason again.

## Functional MV2 check

1. Launch Chrome through `mv2ctl`.
2. Open `chrome://extensions` and enable Developer mode.
3. Choose **Load unpacked** and select the repository's `test-extension` directory.
4. Confirm **MV2 Gate Smoke Test** remains enabled and its toolbar badge reads `MV2`.
5. Open `https://example.com` and run this in DevTools:

   ```js
   document.documentElement.dataset.mv2GateSmokeTest
   ```

   The result should be `"running"`.

The test extension exercises a persistent MV2 background page, `browser_action`, a blocking `webRequest` listener, storage, and a content script.

## Commands

```text
mv2ctl analyze [--chrome PATH] [--dll PATH] [--json]
mv2ctl launch [--chrome PATH] [--timeout SECONDS] [--json] [-- CHROME_ARGS]
mv2ctl smoke-test [--chrome PATH] [--timeout SECONDS] [--json]
mv2ctl functional-ui-test --extension PATH [--chrome PATH] [--timeout SECONDS] [--json]
```

The launcher never modifies Google-signed binaries. Profile recovery is deliberately narrow: it removes only the MV2 disable bit, preserves other disable reasons, discovers the current preference MAC seed from `resources.pak` by validating existing MACs, and updates Chrome's protected preference hashes.
