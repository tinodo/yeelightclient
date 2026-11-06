# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project follows
[Semantic Versioning](https://semver.org/).

## [1.0.0-beta.1] - 2026-11-06

The first release of Yeelight Client, a prerelease for trying out: a complete .NET client for Yeelight smart lights, with the whole of the
[Yeelight WiFi Light Inter-Operation Specification](https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf) behind typed, async C#. Its public API
can still change before 1.0.0, so report what you find in an [issue](https://github.com/tinodo/yeelightclient/issues). A prerelease is not installed by a plain
`dotnet add package YeelightClient`: use `dotnet add package YeelightClient --version 1.0.0-beta.1` (or `--prerelease` for the newest prerelease). The GitHub release of
this version has the package, the test app and the attestation of where the files come from as downloads.

### Added

- **Finding lights**, which works on networks where one method alone does not: a search is sent to the multicast group of the lights and as a broadcast, three
  times, from every network that the computer is on (some lights never hear the multicast group, and answer the broadcast), the announcements that the lights send by
  themselves are listened to, and one address can be asked directly (also across a VPN or on another subnet). `YeelightClient` has `StartDiscovery`, `StopDiscovery`,
  `SearchNow`, `StartListening`, `StopListening`, `DiscoverAsync`, `ProbeAsync`, `FindDevice`, `WaitForDeviceAsync` and `RemoveDeviceAsync`. When the network addresses
  of the computer change (Wi-Fi comes and goes, a VPN connects) the sockets are made again. A message is only believed when the address in it is the address that it
  came from and it is on a local network, a light that is connected is not moved to another address by an announcement, and a client keeps at most
  `YeelightClientOptions.MaxDevices` lights (the protocol has no authentication: use it on a network that you trust).
- **Every method of the specification** as a typed command that takes a `CancellationToken`: power (with a transition and a mode), toggle, brightness, colour
  temperature, RGB (as three numbers or a `Color`), HSV, saving the state as the default, colour flows (`ColorFlow`, `ColorFlowStep`), scenes (`YeelightScene`), adjusting
  without knowing the current value (`AdjustAsync` and the three percentage commands), the name, a sleep timer, `get_prop`, and the background light of a device that has one
  (`device.Background`, the same commands). `SendCommandAsync` sends anything else, as it is. An argument that a light would refuse is refused before anything is sent, a
  method that a light does not list is refused with a `NotSupportedException`, and a command that did not succeed throws a `YeelightException` that says whether the light
  refused it or the way to it failed (`YeelightFailureKind`).
- **A state that follows the light.** A light reports every change to the connections that it has, and the client reads all its properties when a connection is made, so the
  properties of a `YeelightDevice` (and of its background light) are what the light says, whoever changes it. `INotifyPropertyChanged`, `YeelightClient.DeviceStateChanged` and
  `YeelightClient.DeviceDiscovered` tell about changes, raised by a dispatcher of the client: one at a time, in order, never on the thread that reads from a light (a change
  of a property that is still waiting when the same property changes again is raised once, so a slow handler cannot make the queue grow).
- **Music mode** (`StartMusicModeAsync`, `YeelightMusicSession`): the client listens, tells the light where, accepts only the connection of the light itself, and sends commands
  over it without the limit of 60 a minute.
- **A connection that holds up**: one connection per light that is made again with a wait that grows, commands that fail at once when the connection is lost, a keep-alive that
  finds a half open connection, a limit of 60 commands a minute that makes a command wait for its turn, and a connection that follows a light to its new address.
- **Logging** to an `ILogger<YeelightClient>`: every message that is sent and received at the debug level, connections and searches at the information level.
- **`YeelightClient.TestApp`**, a WinUI 3 application to try every command by hand against real lights or against lights that it simulates, to watch the events and the
  wire, and to explore the protocol. It is a download of the release.
- **A package** that is one assembly (`net8.0` and `net10.0`) with its XML documentation, the README, the license and an icon, a symbol package with Source Link, and a build
  provenance attestation that is also a file of the release. The library is compatible with trimming and native AOT.
- **A documentation site**, [tinodo.github.io/yeelightclient](https://tinodo.github.io/yeelightclient/), with guides and an API reference.
- **Tests** against a simulated light on the loopback (no test sends a packet to the real network), of the exact messages of every command, of what a hostile network
  neighbour can send, property-based and fuzz tests, a chaos test that cuts the connection at random, and the project's own tooling.
