# Third-party notices

The repository has no project-wide LICENSE file. An old README badge said MIT but did not constitute a checked licensing decision; that badge has been removed. Ask the repository owner before assuming redistribution rights for the application. Individual dependencies keep their own terms.

| Component | Source / license reference |
|---|---|
| .NET, ASP.NET Core, Identity, EF Core | [dotnet](https://github.com/dotnet), MIT and associated notices in published runtimes |
| Oracle MySQL Connector/NET, EF provider | [Oracle Connector/NET licensing](https://dev.mysql.com/doc/connector-net/en/connector-net-licensing.html); consult package notices and applicable GPL/FOSS exceptions or commercial terms |
| MySQL Community Server | [Oracle MySQL licensing](https://www.mysql.com/about/legal/licensing/), downloaded directly from Oracle; server package is not embedded in the application ZIP |
| Bootstrap 5.3.3 / Bootstrap Icons 1.13.1 | MIT; license files are retained in `wwwroot/lib` |
| QRCoder 1.7.0 | [QRCoder](https://github.com/codebude/QRCoder), MIT |
| Caddy | [Caddy](https://github.com/caddyserver/caddy), Apache-2.0, obtained as an official container image |
| Microsoft Visual C++ 14.44.35211 x64 | Microsoft redistributable terms; official signed installer, SHA-256 pinned in runtime check |

NuGet `packages.lock.json` files record transitive dependencies. Published .NET directories retain their upstream license/notice files. Linux installations pull their own official container images.

`docs/assets/bladez-banner.png` is the exact decorative banner supplied by the project owner. It contains anime-inspired character artwork and is used only in documentation. It is not a product screenshot, original character ownership claim, or evidence of endorsement. Rights to third-party characters remain with their respective owners. Obtain appropriate rights before commercial reuse of that artwork. Actual application avatars/logo are simple code-created SVG assets; no remote stock photo or anime artwork is required by the running app.

The app uses local Bootstrap/icons/system fonts; ordinary page views do not contact a CDN. README shields are GitHub-facing documentation images, not app dependencies.
