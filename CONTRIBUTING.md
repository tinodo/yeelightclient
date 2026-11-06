# Contributing to Yeelight Client

Thanks for your interest in improving Yeelight Client, a .NET client for Yeelight smart lights and their
[LAN control protocol](https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf)!

## Before you start

- Everyone who takes part in this project is asked to follow the [Code of Conduct](CODE_OF_CONDUCT.md). It also says how to report
  behavior that breaks it.
- For bugs or feature requests, please open an issue first using the provided templates so we can discuss the
  approach before you invest time in a PR. A bug report that names the model and the firmware of the light (the `model` and `fw_ver`
  of its answer to a search) and how it was found is worth much more than one that does not: lights differ.
- The [Yeelight WiFi Light Inter-Operation Specification](https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf) is the
  authoritative definition of the messages. Yeelight Client implements it completely, and where real lights do something else
  (and a few do) the behavior of the lights wins: `docs/articles/troubleshooting.md` says what was measured.
- **Lights are not a test bench (hard rule).** A light that somebody uses must not be hit by a test, a tool or a script: the tests run
  against the simulated light of `YeelightClient.Tests/TestSupport` on the loopback (see below), no code of this repository sweeps a subnet or sends a
  loop of commands, and what is tried by hand against a real light is a UDP search and, at most, a read-only `get_prop`. A light takes four connections and
  60 commands a minute on each, and some lights that were driven too hard went on refusing every command until they were switched off at the wall.

## Development setup

- .NET SDK 8.0 and 10.0 (the library multi-targets both; see `YeelightClient/YeelightClient.csproj`).
- `dotnet test YeelightClient.Tests/YeelightClient.Tests.csproj` restores and builds the library and runs the tests. It works on
  Windows, Linux and macOS, and is what CI runs on each of them.
- CI also measures what the tests reach of the library (`--collect:"XPlat Code Coverage"`), and `tools/coverage_summary.py` puts it in the
  summary of the Linux run: the lines and the branches of the library, and the files with the most lines that no test reaches. The same step
  fails when the library is reached for less than the floor in `tools/coverage-floor.json`, so a change that takes tests away, or adds code
  that has none, does not go unnoticed. Raise the floor when the coverage has gone up (keep a margin of about two points: a few tests only run
  on one system); lower it only when code that was reached is gone on purpose, and say why in the pull request. Locally,
  `dotnet test YeelightClient.Tests/YeelightClient.Tests.csproj --collect:"XPlat Code Coverage" --results-directory ./TestResults`, then
  `python tools/coverage_summary.py "TestResults/**/coverage.cobertura.xml" --floor tools/coverage-floor.json` (the number of another system
  differs a little from the one of Linux, which is the one that the floor is for).
- `dotnet build YeelightClient.sln` builds everything, including the WinUI 3 test app (`YeelightClient.TestApp`, see below), so it needs
  Windows and the .NET 10 SDK; on Linux and macOS it fails with NETSDK1100. CI builds the test app on its own Windows job.
- `samples/ConsoleSample` is a small program that uses the library (`dotnet run --project samples/ConsoleSample`, which searches the networks of
  the computer, or `dotnet run --project samples/ConsoleSample -- 192.168.1.50` to ask one address). CI also builds it against the package that was just
  packed, for both target frameworks, the way a consumer of the package does: that is what shows that the package can be used.
- The icon of the package, `YeelightClient/icon.png`, is drawn by `python tools/make-icon.py` (it needs Pillow) from the same drawing as the logo of
  the site, `docs/images/logo.svg`: change them together. `tools/verify-package.py` checks that the package has it, and that it is a PNG that
  nuget.org accepts: a whole file (the checksums of its chunks, chunks in the order, the number and the size that PNG asks for, a valid header, palette
  and transparency, and image data that is as long as its header says, row by row), of at most 1 MB and at least 128 by 128 pixels. It has to be
  a plain image: besides the chunks that every PNG has it may only have `tRNS`, `sRGB`, `gAMA` and `pHYs`, and no metadata (text, colour profiles,
  times, ...), so save it without metadata; `tools/make-icon.py` writes none. The pixels are not decoded: look at the icon when you change it.
- The license of the package is the SPDX expression `MIT` (`PackageLicenseExpression` in `YeelightClient/YeelightClient.csproj`), which is what nuget.org and
  license scanners read; NuGet does not allow a license file next to an expression as the license, so the `LICENSE` file is packed as a plain
  file (the license asks for its notice to be included with copies) and not named as the license. `tools/verify-package.py` checks both: the
  expression in the nuspec, and the file in the package. The copyright line of the package is the one of `LICENSE`, and its release notes are the
  GitHub release of its version (`v` and the `<Version>`).
- The library compiles with its XML documentation as errors (`CS1591` and the other documentation warnings): the comments are the API reference of the
  documentation site. It is built for trimming and native AOT (`IsAotCompatible`), so it uses no reflection and no serializer that needs it.
- The README is also the page of the package on nuget.org, which renders it with Markdig and with less than GitHub does: it drops a relative link
  (the page shows the text without a target) and shows raw HTML as text. So the README uses absolute links
  (`https://github.com/tinodo/yeelightclient/blob/main/CONTRIBUTING.md`) and Markdown only (`**bold**`, not `<b>`), and its images come from a domain
  that nuget.org renders images from (`img.shields.io`, the badge of a GitHub workflow, ...: the list is in the documentation of nuget.org, "Allowed
  domains for images and badges", and the one in `ReadmeTests`). `ReadmeTests` parse it with Markdig as well, in the version that nuget.org runs (see
  *Dependency updates*), so the parser decides what is a link, what is HTML and what is code, and fail on a relative link or image, a link without a
  target, raw HTML and an image from another domain; `tools/verify-package.py` checks that the package has the README. A published version cannot be
  changed, so a mistake in the README stays on nuget.org until the next version.

### The simulated light

The test suite runs against **`SimulatedBulb`** (`YeelightClient.Tests/TestSupport`), a Yeelight light in the process of the test: it answers a search over UDP
and takes control connections over TCP, both on the loopback and on ports that the operating system chooses, and behaves the way the specification says (and the
way the lights that were measured do): a state that the commands change, a `props` report of every change to every connection, the commands that only a light that
is on accepts, a quota of commands on every connection, four connections at most, a sleep timer, scenes, flows, a background light, and music mode (it connects back
to the client). A test makes it misbehave on purpose: `ReplyOverride` (another answer than the right one), `Delay`, `ChunkSize` (messages in pieces),
`CloseImmediately`, `DropConnections`, a light that does not answer a search. The test app links the same file, to offer simulated lights to people without a light.

**Make every client of a test with `TestClients.Create`** (or through `Harness`, which does). It holds the discovery of the client to the loopback: whatever a test
starts, it searches, announces and listens there and nowhere else, so that no test sends a packet to the real network. Never use `new YeelightClient()` followed by
`StartDiscovery()` in a test.

`Harness.StartAsync()` makes a simulated light and a client, finds the light with a search that is sent straight to it, and returns when the light is connected:

```csharp
await using var harness = await Harness.StartAsync();
await harness.Device.SetBrightnessAsync(35);
await Wait.UntilAsync(() => harness.Device.Brightness == 35, "the report of the light");
Assert.Equal(35, harness.Bulb.RequestsOf("set_bright").Single().Parameters[0].GetInt32());
```

**Property-based and fuzz tests** (`YeelightClient.Tests/Fuzz`) use [FsCheck](https://fscheck.github.io/FsCheck/): where an example says that
something works, a property says what has to be true of *all* input, and a generator brings the input. The properties hold for plain functions (the line
reader, the parsers of announcements and messages, the colour flows, the limit of the number of commands); `ReceiveFuzzTests` connect a client to a light that
sends what a light could send but should not (members with the wrong type or missing, ids that match nothing, messages that are cut off, invalid UTF-8, lines of a
megabyte) and check after each that the client is still connected and answers the next command. They run from a fixed seed, 300 cases each, so a run is the same every
time. To look further: `YEELIGHT_FUZZ_CASES=20000 YEELIGHT_FUZZ_SEED=7 dotnet test YeelightClient.Tests --filter Category=Fuzz` (with `--logger
"console;verbosity=detailed"` it says what was tried). When a campaign finds something, keep the case as an example next to the fix: a fixed seed does not repeat it for ever.

**The chaos test** (`YeelightClient.Tests/Chaos`) puts a proxy (`ChaosProxy`) between the client and a simulated light that cuts the connection with a reset, stalls
it and refuses it, at random, while the client keeps sending commands. It has to go on working: every call ends within its time limit, in a failure that says that the
connection is gone; the client is connected again, and does what it did, when the trouble stops; and its memory and its threads do not grow with the number of
connections. It runs for five seconds on every build, and `YEELIGHT_SOAK_SECONDS=600 dotnet test YeelightClient.Tests --filter Category=Soak` runs it for as long as you say; the
seed is in its output, and `YEELIGHT_SOAK_SEED` runs the same chaos again.

### The test app

`YeelightClient.TestApp` is a WinUI 3 application (Fluent 2: Mica, a navigation pane, theme colors, the type ramp of Windows 11) whose
only purpose is to **try every command and event of the library by hand against real lights, and to explore the protocol**. It is not part of the
NuGet package, and it only uses the public API of the library.

| Page | What it does |
| --- | --- |
| Lights | The lights of the client, live: name, model, address, whether they are connected, on or off, brightness and colour. Search now, search every so many seconds, listen to announcements, ask one address, forget a light, and start **simulated lights** to try the app without one. Nothing is sent to the network until you do one of these. |
| Control | Every command of a light: power, brightness, colour temperature, RGB and HSV, scenes, a colour flow editor, adjust, the sleep timer, the name, the background light and music mode. The outcome of every command (or the exception) is shown, and the state follows the light. |
| Raw | Any command, by method and parameters as JSON, with its answer and its time; a reference of the methods of the protocol. |
| Events | Every event as the client raises it. |
| Log | Everything the client logs, the messages on the wire included; export as JSON lines. |
| Settings | The options of the client, and the theme. |
| Help | What the app is for, the versions of the app, the library and the runtime, how to switch on LAN Control, and the keyboard shortcuts. |

Build and run it on Windows with the .NET 10 SDK: `dotnet run --project YeelightClient.TestApp -p:Platform=x64`. Every release has it as a
download (`YeelightClient.TestApp-<version>-win-x64.zip`), and every build of CI publishes it as the artifact `YeelightClient.TestApp-win-x64`
(kept for two weeks). Both are unpackaged and self-contained: unzip it and start `YeelightClient.TestApp.exe`; nothing else has to be
installed. CI also starts the app with `--smoke-test <log>`, because the XAML of an app is only read when it
runs: the app opens every page, builds, fills in and reads the editors, and then goes the whole way against a simulated light on the loopback: it finds it,
sends commands through its own pages, and checks what the light received and what the Events and Log pages show. It never uses the real network. The pictures
it takes of the pages are the artifact `test-app-smoke-test`, which is how the design can be looked at without running the app.

### The documentation site

The site at <https://tinodo.github.io/yeelightclient/> is built with [DocFX](https://dotnet.github.io/docfx/), which is pinned in
`.config/dotnet-tools.json`.

| Where | What |
| --- | --- |
| `docs/index.md` | The home page. |
| `docs/articles/` | The guides, in Markdown; its `toc.yml` is the table of contents. |
| `docs/api/index.md` | The introduction of the API reference. The reference itself is **generated** from the XML documentation comments in the source of `YeelightClient`; the generated YAML is not committed. |
| `docs/template/public/` | The theme: `main.css` and `main.js`, on top of the modern template of DocFX. |
| `CHANGELOG.md` | Copied to the site as its changelog page (the copy, `docs/changelog.md`, is not committed). |

Build it, and look at it, on any platform with the .NET SDKs (8.0 and 10.0) and Python 3:

```shell
python tools/docs/build.py --serve     # builds docs/_site, checks its links, and serves it at http://localhost:8080
```

The build fails on a DocFX warning (such as an `xref:` that cannot be resolved or a link to a file that does not exist) and on a link of the
result that leads nowhere. A pull request that changes `docs/`, the library, the
changelog or the tooling runs the **Docs** workflow, which builds the site and uploads it as the artifact `docs-site`; a push to `main`
publishes it to GitHub Pages (Settings → Pages is set to deploy from GitHub Actions).

Documentation comments (`///`) are documentation: they become the API reference. Say what a member does and returns, say when it throws,
and cross-reference with `<see cref="…"/>`. When a change alters how the library is used, update the article that explains it too.

## Making changes

- Keep changes focused; avoid unrelated refactors in the same PR.
- Follow the existing code style (see `.editorconfig`) and the patterns already used in neighboring files.
- Add or update XML doc comments for any public API you touch.
- Add tests for new behavior and for bug fixes (a regression test that fails before your fix and passes after it).
- Update `README.md` if you change public API surface or behavior, and the articles in `docs/` if you change how the library is used.
- A change that users can see goes in `CHANGELOG.md` under the unreleased version.
- Start closed: a new class is `sealed`, and a new type is `internal` unless an application needs it. Opening one later breaks nobody, but a
  type that is public cannot be made internal, and a class that others can derive from cannot be sealed, without breaking someone. The
  tests in `YeelightClient.Tests/Unit/PublicApiTests.cs` fail for a class that is not sealed and for a public type that is not on their list;
  one that should be public or open on purpose is added to them in the same pull request, with the reason.
- The numeric values of the public enums are embedded in assemblies that use them: when you add a member to one, add it **at the end**
  (`EnumStabilityTests` guard the values).
- Nothing the library does may block the thread that reads from a light with code of the application: events go through the dispatcher.

## Pull requests

- The checks of `main` must pass before a PR can be merged: *Build & Test* on Ubuntu and Windows, *Build Test App (WinUI 3)*, *CodeQL* and
  *Pack (single assembly)*, on a branch that is up to date with `main`. Every conversation on the pull request, the comments of the Copilot
  review included, has to be resolved. The other checks (macOS, the docs build, the dependency review) are not required, but read them: they are
  there for a reason.
- GitHub Copilot automatically reviews pull requests in this repository; please address or respond to its comments.
- Describe what changed and why; link the related issue if any.

## Dependency updates

Dependabot (`.github/dependabot.yml`) looks at the GitHub Actions of the workflows and at the NuGet packages (the projects of
`YeelightClient.sln` and the DocFX tool of the documentation) every Monday morning. The routine updates of each ecosystem come in one pull
request: all the actions together, and all the minor and patch updates of the packages together. A major update of a package gets a pull
request of its own, so that it can be read before it is merged; the default limit of five open pull requests per ecosystem is not changed,
because a lower one would let an unmerged major update hold back the routine ones. A package or action release has to be a week old before it
is proposed (the *cooldown*), which keeps one that was published by mistake, or with malicious code, out of the repository in most cases.
Security updates are not delayed. Dependabot alerts and security updates are on.

**The actions are pinned.** Every `uses:` in `.github/workflows` is the full commit SHA of a release, with the version in a comment
(`uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1`), and the repository setting *Require actions to be pinned to a
full-length commit SHA* makes a workflow fail when one is not. A tag of an action can be moved to other code, a commit cannot, which matters most
for `publish.yml`: its jobs hold the token that becomes a nuget.org API key. When you add or change a step, look the SHA up with
`gh api repos/OWNER/REPO/commits/vX.Y.Z --jq .sha` (the tag in the comment has to point at it); Dependabot keeps both the SHA and the comment
current, so every release of an action, also within a major version, comes as a pull request.

These are pull requests like any other: CI builds and tests them, and the Docs workflow builds the site when DocFX is updated. Read the
release notes of a major update before you merge it, and be careful with the actions of `publish.yml`: a change to that workflow is only
exercised when a version tag is pushed, or by a dry run (see *Releasing*), so do a dry run after you merge an update of one of its actions.

**Dependency review.** The *Dependency review* workflow (`.github/workflows/dependency-review.yml`) runs
[`actions/dependency-review-action`](https://github.com/actions/dependency-review-action) on every pull request: it compares the
dependencies of the pull request (the NuGet packages of the projects and the actions of the workflows) with those of `main`, and fails when
the change brings in a package with a known vulnerability of moderate severity or worse, whatever scope the dependency graph gives it. It only sees
what the dependency graph of GitHub lists, and that does not include the DocFX tool of the documentation (`.config/dotnet-tools.json`): Dependabot still
proposes its new releases, but this review does not check them for vulnerabilities, so read the release notes of a DocFX update (it builds the site, and
nothing of it ships in the package). Nor does it find a vulnerability in an action: GitHub documents that its alerts for actions are only
generated for actions that use semantic versioning, not SHA versioning, and the actions here are pinned to a commit (see above), so read the
release notes of an update of an action too (the pinning stays: it is what keeps a moved tag out). The review is advisory, like the macOS
job: it is not one of the required checks of `main`, because it needs the dependency data that GitHub computes after a push (the workflow
waits for it for up to three minutes), and a check that waits for a service should not be able to hold up every merge. Read what it says
before you merge a pull request that it failed.

The one dependency of the library, `Microsoft.Extensions.Logging.Abstractions`, is part of what its users get with it, so it is asked for at
the oldest version that has what the library uses (8.0.0): a library that asks for a newer one makes every application that uses it
update too. No version update is proposed for it, whatever the version (a security update still is, see `.github/dependabot.yml`);
raise it by hand when the library needs something that a newer version has.

**Markdig is held at the version that nuget.org runs.** `ReadmeTests` (in `YeelightClient.Tests`) parse the README the way nuget.org does, so the
Markdig that they use has to be the one of nuget.org, and that is not the newest: `Markdig.Signed` 0.37.0 in the `Directory.Packages.props` of
[NuGetGallery](https://github.com/NuGet/NuGetGallery), checked on 2026-11-02 (`Markdig` 0.37.0 is built from the same source commit). A newer
version can read something differently from what nuget.org shows, and a published version cannot be changed. So no version update is proposed
for it (a security update still is, see `.github/dependabot.yml`), and a test fails when its version is changed. When NuGetGallery raises it
(`gh api repos/NuGet/NuGetGallery/contents/Directory.Packages.props -H "Accept: application/vnd.github.raw"` shows it), raise the version in
`YeelightClient.Tests/YeelightClient.Tests.csproj` and in `ReadmeTests`, and run those tests.

## OpenSSF Scorecard

The *Scorecard* workflow (`.github/workflows/scorecard.yml`) runs the [OpenSSF Scorecard](https://scorecard.dev) on every push to `main` and every
Wednesday: automated checks of how the project is run (pinned actions, the permissions of the workflow tokens, dependency updates, code scanning, a
security policy, and more), each with a score from 0 to 10. The findings are in the code scanning alerts of the repository (*Security*, *Code
scanning*, the tool *Scorecard*), and the results are published at <https://scorecard.dev/viewer/?uri=github.com/tinodo/yeelightclient>. The score is a
prompt for improvements and not a gate: nothing fails a pull request because of it. Some of the checks cannot be met by a project with one
maintainer (a second reviewer for every change, contributors from several organizations), so a score of 10 is not the goal: the findings that
can be acted on are.

Scorecard only accepts the results of a workflow that keeps to its rules: no `env` or `defaults`, no write permission on the workflow as a whole,
`id-token: write` on its one job, and in that job only the actions checkout, upload-artifact, codeql-action/upload-sarif and scorecard-action.
So do not add steps to `scorecard.yml`. Its *Token-Permissions* check also looks at the other workflows: they ask for read access at the top
(`permissions: contents: read`) and give a write permission to the one job that needs it.

## Repository settings (maintainers)

These settings are not in the repository, so this table is where they are written down: if you change one, change the table too (`gh api
repos/tinodo/yeelightclient/rulesets` and `gh api repos/tinodo/yeelightclient/actions/permissions/workflow` show what is set).

| Where | What | Why |
| --- | --- | --- |
| Settings, Rules: *Require CI and Copilot review on main* | A pull request is required (no approval: there is one maintainer). The checks listed under *Pull requests* must pass on a branch that is up to date, and the conversations must be resolved. Copilot reviews every push. `main` cannot be deleted or force-pushed. The owner can bypass the rules | `main` only gets what CI has built, tested and scanned. The bypass is for a broken pipeline, not for a pull request whose checks have not passed |
| Settings, Rules: *Release tags are immutable* | The tags `v*` cannot be deleted or moved. The owner can bypass, which the recovery under *When it fails* needs | A tag names what was published and what the release was built from: the publish workflow checks that it did not move |
| Settings, Actions, General | The token of a workflow is read-only by default (each workflow and job asks for what it needs with `permissions:`), a workflow cannot approve a pull request, and an action must be pinned to a full-length commit SHA | A workflow, or an action in it, only gets what it asks for, and cannot approve its own pull request |
| Settings, General | The head branch of a pull request is deleted when it is merged. Squash, merge and rebase are all allowed. Discussions are on; the wiki and the projects are off | No stale branches; the documentation is in the repository |
| Settings, Code security | Dependabot alerts and security updates, secret scanning with push protection, and private vulnerability reporting are on | See `SECURITY.md` and *Dependency updates* |
| Settings, Pages | Source: GitHub Actions | The *Docs* workflow deploys the site |
| Settings, Environments | `release` (see *Releasing*) and `github-pages` | |

## Releasing (maintainers)

Publishing is one workflow, `.github/workflows/publish.yml`, started by pushing a version tag. It checks the version and the
changelog, packs **once** (the package, and next to it its symbols), checks that the package is a single assembly, builds the test app,
attests where the files come from (see *Build provenance*) and writes the notes of the release. Then it waits for your approval, pushes
that same `.nupkg` to nuget.org and, once that lists it, to GitHub Packages, and creates the GitHub release with the notes, the package,
the test app and the attestation of the files (`YeelightClient.X.Y.Z.intoto.jsonl`, see *Build provenance*).

**Trusted Publishing, in short.** No NuGet API key is stored anywhere. When the nuget.org job runs, GitHub signs a token that says
"this is the workflow `publish.yml` of `tinodo/yeelightclient`, running in the environment `release`". nuget.org checks that against a
*policy* that the package owner created, and answers with an API key that is valid for one hour. Anything else (another
repository, another workflow, another environment) gets nothing.

### One-time setup

| Where | What | State |
| --- | --- | --- |
| GitHub: Settings, Environments, `release` | Required reviewer (you approve each publish), deployments only from `v*` tags and from `main` | done |
| GitHub: environment secret `NUGET_USER` | The nuget.org **profile name** of the package owner: `tinod`. Not the GitHub name `tinodo`, and not an e-mail address | done |
| nuget.org: Account, Trusted Publishing, Add policy | Repository Owner `tinodo`, Repository `yeelightclient`, Workflow File `publish.yml` (file name only, not the path), Environment `release`. Scopes: publish **new packages** and **new versions**, for the package `YeelightClient` only | **only the package owner can do this** |

A policy for a public repository is active at once. The package ID `YeelightClient` is not taken yet (checked on 2026-11-06): the first push of the first release
creates it under the owner of the policy, and from then on the policy only needs the scope for new versions.

### Each release

1. Bump `<Version>` in `YeelightClient/YeelightClient.csproj` (and the version in the *Installation* of the README), write the section of that
   version in `CHANGELOG.md` and give it its date (`## [1.0.0-beta.1] - 2026-11-06`), and merge to `main` with a pull request. That
   section is the text of the GitHub release: what is new, what breaks, what is fixed. A version without a section that has a date is
   refused by the first job of the workflow, before anything is built. Read what the docs say about the version as well:
   `git grep -n -i -E "\bbeta\b|prerelease|release candidate"` (outside the changelog, the tools, the tests and the workflows) finds
   the README and the pages of the docs that call the current version a beta, a release candidate or a prerelease, or that tell how to
   install it. What is history (what happened in an earlier version) stays.
2. Optional, and worth it after a change of the workflow or of one of its actions: a **dry run**. Actions, *Publish Package*, *Run
   workflow*, pick the branch (or `main`; never a tag, which the workflow refuses), tick *Dry run*. It does everything up to the approval,
   for what is on that branch: it checks the version and the changelog, packs, builds the test app, attests and verifies the files, shows
   the release as it will be made in the summary of the run, and makes a draft release to prove that one can be made (which is deleted
   again). It publishes nothing and needs no tag.
3. `git switch main && git pull && git tag vX.Y.Z && git push origin vX.Y.Z` (the tag has to equal `<Version>`). A release is only ever
   made by a run of its own tag, because the attestation of its files (see *Build provenance*) says which ref and which commit the run
   was for: the push starts it, and a run that is started by hand (*Run workflow*) has to have *Use workflow from* set to the tag, and
   not to a branch (the first job refuses it). There is no input for a tag.
4. Open the run in the Actions tab. The *Check the version*, *Pack*, *Build the test app* and *Prepare the release* jobs run first (the
   package and the test app are artifacts of the run; download them if you want to look). The *Publish to NuGet.org* job then waits for
   you: **Review deployments**, **Approve and deploy**. That one approval is the gate of every channel: GitHub Packages and the release
   follow once nuget.org lists the version.
5. The *Publish to NuGet.org* job checks that the tag still points at the commit that was packed, pushes the package, **waits until
   nuget.org lists the version** (it validates a package first, which takes a few minutes), **checks that the package that nuget.org
   serves is the one that was packed** (`tools/compare-published.py`: file by file, nuget.org adds only its signature), and then pushes
   the symbols. *Publish to GitHub Packages* follows. When both are done, the *Create the GitHub release* job makes the release: titled
   with the tag, a prerelease when the version has a suffix, with the notes, three files, `YeelightClient.X.Y.Z.nupkg` (the package that was
   published), `YeelightClient.TestApp-X.Y.Z-win-x64.zip` (the test app) and `YeelightClient.X.Y.Z.intoto.jsonl` (the attestation, see *Build
   provenance*), and the SHA-256 of all three in its text. Have a look at it.

Only one run for a version is active at a time (the workflow has a concurrency group per tag): a second run for the same tag waits for
the first to end, and then stops at the first job, because nuget.org lists the version by then. A run that waits for your approval holds
its turn: cancel it before you start over.

**Prereleases.** A version with a suffix (`1.0.0-beta.1`, tag `v1.0.0-beta.1`) goes through the same workflow. nuget.org lists it as a
prerelease, which `dotnet add package YeelightClient` and the NuGet UI skip unless they are asked to include prereleases, so what people
get by default stays the latest stable version. A version cannot be reused: a fixed beta is `1.0.0-beta.2`, and the stable release is `1.0.0`.

**Symbols.** Next to the package the workflow builds `YeelightClient.X.Y.Z.snupkg`: the debug symbols (PDB) of each target framework, with
[Source Link](https://learn.microsoft.com/dotnet/standard/library-guidance/sourcelink), so that a debugger can step into the library and
show the source of the commit that was built. nuget.org serves them from its symbol server (Visual Studio: *Tools, Options, Debugging,
Symbols*, tick *NuGet.org Symbol Server*). `tools/verify-package.py` makes sure that the symbol package is there, that it holds nothing
but the PDBs, and that they carry Source Link (and that the package itself has no PDB). The symbols are pushed once the package is
listed, and a failure to push them is a warning, not a failed release.

**Build provenance.** The *Prepare the release* job makes a signed
[build provenance attestation](https://docs.github.com/en/actions/concepts/security/artifact-attestations) (`actions/attest`) for the
package, its symbols, the test app and the two libraries in the package (`lib/net8.0/YeelightClient.dll`, `lib/net10.0/YeelightClient.dll`), and
then checks each of them with `gh attestation verify`, before anything waits for your approval. An attestation is GitHub's statement
that this workflow of this repository made a file, from this commit and ref, on a GitHub-hosted runner. It is signed with a certificate
that Sigstore issues for that one run, and kept with the repository (and, as the repository is public, in a public transparency log). It
is made in a job of its own: that job restores and builds nothing, and the jobs that do have no `id-token` permission, so no package that
a build pulls in can ask for the token that signs. This is how anyone checks a download, and the notes of every release say so, with its
version:

```shell
gh attestation verify YeelightClient.X.Y.Z.nupkg --repo tinodo/yeelightclient --signer-workflow tinodo/yeelightclient/.github/workflows/publish.yml --source-ref refs/tags/vX.Y.Z
```

The attestation is a file of the release as well: *Prepare the release* writes the Sigstore bundle of the one attestation (it covers all the
files) as `YeelightClient.X.Y.Z.intoto.jsonl` (JSON Lines, and the name that tools and the OpenSSF Scorecard look for as a provenance file), checks
every file against that file too (`gh attestation verify <file> --bundle YeelightClient.X.Y.Z.intoto.jsonl ...`, which does not ask GitHub for
the attestation), and the release has it as an asset, with its SHA-256 in the notes. So the evidence stays with the release, and a download
can be checked from the release alone.

`--source-ref` is what tells a release from a dry run. A release is only made by a run of a tag, and a dry run is only of a branch (the
first job refuses the other way round, each), so the ref of an attestation says which of the two made the files, and the command above,
which asks for a tag, never accepts the files of a dry run. nuget.org signs the package that it serves,
which changes that file and not what is in it: the `.nupkg` from nuget.org does not verify (the one on the release page does), and the
libraries in it do. After a restore, the same command on `~/.nuget/packages/yeelightclient/X.Y.Z/lib/net8.0/YeelightClient.dll` checks the library
that runs.

### When it fails

Re-run the **failed jobs** in the Actions tab (not *all jobs*, and not a new run for the tag): they use the commit and the artifacts of
that run (kept for 90 days), so what is finished is what was built. Never delete and re-tag once something was published; the one case in which a tag
may be moved is in the table (the first job refuses the version, so nothing was published).

| Symptom | Cause |
| --- | --- |
| `NuGet/login` fails with 403 | The job lacks `id-token: write` (it has it), or `NUGET_USER` is wrong: it must be the profile name `tinod` |
| No matching policy | The policy on nuget.org differs from the workflow: owner, repository, file name (`publish.yml`) or environment (`release`) |
| Push is unauthorized | The policy is not owned by the account that owns the package, or its scope does not allow new packages (the first release) or new versions of `YeelightClient` |
| The job never starts | It is waiting for your approval |
| The first job (*Check the version*) refuses the release | The run is of a branch and not of a tag (push the tag, or start the workflow with *Use workflow from* set to the tag; to try it on a branch, tick *Dry run*), or it is a dry run of a tag (a dry run is of a branch), or the tag is not `vX.Y.Z` (or `vX.Y.Z-suffix`), differs from `<Version>`, is not on `main`, or `CHANGELOG.md` has no section for the version, or the section has no date (`Unreleased`). Nothing was published. Fix it on `main`, move the tag to the new commit (`git push origin :refs/tags/vX.Y.Z`, `git tag -d vX.Y.Z`, tag again), and push it |
| The first job says that nuget.org lists the version already | A version is never built again: the package that was published can never be replaced, so a new build could not be matched with it (the symbols, the test app and the files of the release would come from another build). To finish a release that failed after the push, re-run the **failed jobs** of its run |
| *Verify the tag still points at the commit that was packed* fails | The tag was moved while the release waited for your approval. Nothing was published (GitHub Packages and the release wait for nuget.org, so this is the first thing that is checked); release a new version |
| *Verify the tag still points at the commit that was released* fails (in *Create the GitHub release*) | The tag was moved after the package was published. The release was not made. Move the tag back to the commit that the message names (`git push --force origin <commit>:refs/tags/vX.Y.Z`) and re-run the job, or make the release by hand |
| *Wait until NuGet.org lists the version* fails | nuget.org did not list the version within 30 minutes: it may have failed its validation (look at the page of the version; you also get an e-mail). Re-run the job when it is fine |
| *Attest where the files come from* fails (in *Prepare the release*) | Most likely the job lacks `id-token: write` or `attestations: write` (it has both), or GitHub or Sigstore did not answer. Nothing was published: the approval comes after this job. Re-run the failed job |
| *Verify the attestations* fails (in *Prepare the release*) | GitHub has no attestation for a file that matches this run (the output of the failed file says which check did not hold: the workflow, the ref, the commit, a self-hosted runner, or no attestation at all). Nothing was published. Re-run the failed job, which makes the attestations again |
| *Verify that NuGet.org serves the package that was packed* fails | nuget.org lists this version, but not the package that this run packed: another run, or an earlier attempt for the same version, published first (the push skips a duplicate). The step names the files that differ. Nothing after it ran: no symbols, no GitHub Packages, no release. Find the run that published, and if that package is the wrong one, release a new version (a published version can never be replaced) |
| A warning that the symbols were not pushed | The package is published. Re-run the *Publish to NuGet.org* job: it skips the package as a duplicate and pushes the symbols |
| *Create the GitHub release* fails | Re-run it. It updates a release that exists (a draft too: it is published after its files are uploaded), so it is safe after a release that was made by hand. A release is only made the latest one when its version is the newest stable one |

A published version is **permanent**: nuget.org lets you unlist or deprecate it, never delete or replace it. If a release is wrong,
publish a fixed one.
