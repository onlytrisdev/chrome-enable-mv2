# Browser process identity (issue #15, v3.14.0)

Helium and other Chromium browsers can use the process name `chrome.exe`.
That name now selects candidates, not browser identity. `ChromeProcessScope`
queries each candidate's executable path with limited process-query access.

| Candidate | Launch and status policy |
| --- | --- |
| Same selected executable | Keep the existing running-Chrome checks. |
| Proven different executable | Ignore it, without reading its modules or patch bytes. |
| Executable cannot be identified | Block launch, reuse, and automatic restart. |

The GUI status, launch guard, session inspection, reuse, and restart completion
use the same scope. Session APIs receive the selected `ChromeInstallation`;
they do not silently switch back to the default Chrome installation.
Restart validates the current on-disk version of that selected executable
before closing a running session.

Profile, Windows user/session, PID creation time, and complete RAM-byte checks
remain required for reuse/restart. A different profile of the same executable
is not automatically closed or treated as a reusable session. This change does
not add a Beta/Dev browser picker or promise MV2 support for unverified builds.

The functional-test patcher, folder-dialog lookup, and restart cleanup use only
the test browser's proven process tree. PIDs are pinned while owned processes
are used. A same-installation peer is not an owned descendant. Child modules
must also match the test launch's DLL path before the helper writes RAM.

## Verification

The foreign-process regression failed against the old name-only guard with
`Chrome is already running`, then passed after executable-scoped discovery.
The fixture is Windows `ping.exe`, copied into an isolated temporary directory
as `chrome.exe`. Tests stop only their own fixture.

The local validation on 2026-11-16 includes:

- 142 .NET tests: 133 portable and 9 integration tests.
- 16 self-contained GUI checks with a live foreign `chrome.exe`, including
  launch, windowless reuse, vi/en/zh-CN status, and normal/forced restart consent.
- Actual MV2 Load unpacked installation and background-page persistence after
  full restart on Chrome 158.0.8127.36 while the namesake remains alive.
- Two real Chromium installations: selected Chrome 158.0.8127.36 and portable
  Chrome 156.0.8078.12. The portable browser stays alive through the selected
  browser's MV2 restart, with its planned patch bytes unchanged. Explicit
  session inspection/closure also works for the nondefault executable.

These are process-isolation tests, not a direct test of a Helium build.

### Run the checks

Close user Chrome sessions before running the desktop/process test suite.
The GUI script creates and owns the extra namesake when requested:

```powershell
.\scripts\test-gui-launch.ps1 -WithForeignBrowser
```

The legacy-plan comparison needs a known legacy-supported Chrome fixture.
The installed Chrome can evolve past the legacy signatures while automatic
discovery still succeeds. To use separate legacy and fresh-package fixtures:

```powershell
$env:MV2_TEST_LEGACY_CHROME = 'E:\Fixtures\chrome156\chrome.exe'
$env:MV2_TEST_GUI_ARCHIVE = 'E:\Fixtures\current-build\gui.zip'
dotnet test .\ManifestV2.slnx -c Release
```

The GUI archive must contain the entire self-contained output and match the
current assembly version. Both fixture assertions remain mandatory; the
environment variables select inputs, not weaker validation.
