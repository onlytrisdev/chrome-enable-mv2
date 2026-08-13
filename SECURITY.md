# Security and antivirus notes

Chrome MV2 Launcher never changes `chrome.dll` on disk. It starts Chrome under the Windows debugging API, verifies a semantic patch target, and changes a small verified set of bytes in the new Chrome process only.

Those debugging and process-memory APIs are also used by debuggers, profilers, accessibility tools, and malware. Heuristic antivirus products can therefore flag an unsigned build even when it was built from this public source.

## Verify a release

1. Download the ZIP and `SHA256SUMS.txt` from the same GitHub Release.
2. Run:

   ```powershell
   Get-FileHash .\ChromeMv2Launcher-v3.0.0-win-x64.zip -Algorithm SHA256
   ```

3. Compare the result with `SHA256SUMS.txt`.
4. You can also build the project yourself with `scripts\build-release.ps1`.

The release uses a normal ZIP rather than a self-extracting single-file executable. All .NET and Windows App SDK dependencies are included in the extracted folder, so no separate runtime installation is required.

## SmartScreen and code signing

The current binaries are not Authenticode-signed. A checksum proves that a download matches the published release, but it does not create Windows SmartScreen reputation. The correct long-term solution is an OV or EV code-signing certificate (or a trusted managed signing service). A self-signed certificate is intentionally not used because it does not establish public trust and can make warnings more confusing.

## Reporting a detection

Please open a GitHub issue with the release version, exact file SHA-256, antivirus product, detection name, and a screenshot or report URL. Do not upload private Chrome profiles or extension data.
