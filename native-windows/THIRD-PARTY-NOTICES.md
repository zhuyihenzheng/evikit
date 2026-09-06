# Third-party components

The binary distribution contains these components. This notice identifies their upstream licenses; it is not a new license grant for evikit itself. evikit has no separate public-distribution license at this time.

| Component | Version | License / upstream |
| --- | --- | --- |
| .NET runtime and base libraries | 10.0 (exact patch in `evikit.deps.json`) | MIT and third-party notices: https://github.com/dotnet/runtime |
| Windows Forms / Windows Desktop runtime | 10.0 (exact patch in `evikit.deps.json`) | MIT and third-party notices: https://github.com/dotnet/winforms |
| YamlDotNet | 16.3.0 | MIT: https://github.com/aaubry/YamlDotNet/blob/v16.3.0/LICENSE.txt |

The .NET SDK is a build dependency; it is not part of the app distribution. SDK telemetry is disabled by `build.ps1`.

Copies of upstream license and third-party notice files are included in `licenses/` in the prepared release folder. Preserve these files when redistributing an approved internal build. Windows itself is an operating-system prerequisite and is not redistributed here.
