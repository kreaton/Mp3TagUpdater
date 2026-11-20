# Third-party notices

**Mp3TagUpdater** is © 2026 Per Hejndorf and is licensed under the [MIT License](LICENSE), except where stated below. This file lists the third-party components and assets it uses, each under its own licence. Licence texts and copyright notices are kept by the upstream projects at the *Source* links; where a licence requires its notice to travel with binaries, the upstream package already carries it.

_Last reviewed: 2026-11-17. Versions are the ones resolved in this repository at that date; Dependabot keeps them current, so check the project files for the exact version of a given release._

## Libraries included in the application

| Library | Version | Licence | Source | Modified |
|---|---|---|---|---|
| TagLibSharp | 2.3.0 | LGPL-2.1-only — used unmodified as a separate, replaceable `TagLibSharp.dll` | [NuGet](https://www.nuget.org/packages/TagLibSharp/2.3.0) · [source](https://github.com/mono/taglib-sharp) | No |
| Velopack | 1.2.161 | MIT | [NuGet](https://www.nuget.org/packages/Velopack/1.2.161) · [source](https://github.com/velopack/velopack) | No |

## Build, test and tooling (not distributed)

Used to build, test or render promotional material only; not part of the shipped application.

| Library | Version | Licence | Source | Modified |
|---|---|---|---|---|
| .NET Desktop Runtime (prerequisite, installed separately; not bundled) | 10.0 | MIT | [dotnet/runtime](https://github.com/dotnet/runtime) | No |
| coverlet.collector | 6.0.4 | MIT | [NuGet](https://www.nuget.org/packages/coverlet.collector/6.0.4) · [source](https://github.com/coverlet-coverage/coverlet) | No |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | [NuGet](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/17.14.1) · [source](https://github.com/microsoft/vstest) | No |
| xunit | 2.9.3 | Apache-2.0 | [NuGet](https://www.nuget.org/packages/xunit/2.9.3) · [source](https://github.com/xunit/xunit) | No |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 | [NuGet](https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.4) · [source](https://github.com/xunit/visualstudio.xunit) | No |

## Icons, images, audio and video

| Asset(s) | Origin | Licence |
|---|---|---|
| `Mp3TagUpdater.Wpf/app.ico`, `images/banner.png` | The tile and music note are reused from the author's Audiofile Player (formerly MP3 Player) icon, which the author AI-generated with an image-generation tool. The green update badge, the layout and the banner were drawn in code (Python with Pillow) with Claude Code, under the author's direction. The banner text is set in Segoe UI from the Windows fonts folder and is rendered into the image; no font file is distributed | MIT (as part of this project) |
| `Mp3TagFromApi.Wpf/app.ico` | The Mp3TagUpdater icon above, with the green update badge replaced by a yellow lightning bolt on a dark blue disc, drawn in code (PowerShell with System.Drawing) with Claude Code, under the author's direction | MIT (as part of this project) |

## Online services (Mp3TagFromApi)

Mp3TagFromApi reads album data from the [Spotify Web API](https://developer.spotify.com/documentation/web-api) and the [Discogs API](https://www.discogs.com/developers), using the user's own keys. No data from either service is stored in this repository. The Spotify and Discogs terms apply to that use. Where Discogs data is shown, the app says "Data provided by Discogs.", as Discogs asks.

The test suite writes its own mp3 files at run time; none are stored in the repository.

## Trademarks

Microsoft, Windows, .NET and other Microsoft product names are trademarks of the Microsoft group of companies. Spotify is a trademark of Spotify AB. Discogs is a trademark of Zink Media, LLC. All other product names, logos and brands mentioned in this repository are the property of their respective owners. They are used only to identify the services and tools this project works with, and their use does not imply endorsement.
