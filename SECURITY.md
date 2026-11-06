# Security Policy

## Supported Versions

Only the latest published release of `YeelightClient` on [NuGet](https://www.nuget.org/packages/YeelightClient) is supported
with security fixes. Please upgrade to the latest version before reporting a vulnerability.

## Reporting a Vulnerability

Please **do not** open a public GitHub issue for security vulnerabilities.

Instead, use GitHub's private vulnerability reporting for this repository:
[Report a vulnerability](https://github.com/tinodo/yeelightclient/security/advisories/new).

Please include:

- A description of the vulnerability and its potential impact.
- Steps to reproduce, or a minimal repro project.
- The version of `YeelightClient` and the target framework/OS you tested against.
- When it involves a light: its model and firmware version (`model` and `fw_ver` in its discovery answer).

We'll do our best to acknowledge your report promptly and keep you updated as we investigate and address the issue.

## Scope notes

`YeelightClient` is a client for [Yeelight](https://www.yeelight.com/) smart lights on the local network. It speaks to the lights with the
LAN protocol of the [Yeelight Inter-Operation Specification](https://www.yeelight.com/download/Yeelight_Inter-Operation_Spec.pdf):
discovery listens for the UDP multicast and broadcast announcements of the lights (and sends searches of its own), a light is controlled
over a TCP connection, and in music mode the client listens on a TCP port that the light connects to. The protocol has neither authentication
nor encryption, so anyone on the same network can find and control a light that has LAN control turned on: that is how the lights work, and
not something that this library can change. Within that, the client believes an announcement only when the address in it is the address that it
came from and is on the local network, does not move a light that it is connected to to another address, and keeps at most
`YeelightClientOptions.MaxDevices` lights.

What is security-relevant is how the client handles what it receives. A light, a device that pretends to be one, and any other machine on
the network can send anything to the multicast group and to the ports of the client, so connection handling must not trust what a device or
a network neighbour sends. A malformed, oversized or hostile announcement or reply, an address or a port that leads somewhere else than to
the light that was asked for, a connection to the music mode port that does not come from the light, and a flood of datagrams or
connections must never make the client act on what it should not, crash, hang, or run out of memory or sockets. Reports of such cases are
in scope. A weakness of the protocol itself, or of the firmware of a light, is for Yeelight.

## Verifying a release

The package and the test app of every release are built by the `publish.yml` workflow of this repository, on GitHub-hosted runners, and
have a signed [build provenance attestation](https://docs.github.com/en/actions/concepts/security/artifact-attestations): GitHub's
statement of which workflow built the file, from which commit. The libraries inside the package have one as well. Check a file you
downloaded from a [release](https://github.com/tinodo/yeelightclient/releases) with the [GitHub CLI](https://cli.github.com/):

```shell
gh attestation verify YeelightClient.1.0.0-beta.1.nupkg --repo tinodo/yeelightclient --signer-workflow tinodo/yeelightclient/.github/workflows/publish.yml --source-ref refs/tags/v1.0.0-beta.1
```

nuget.org signs the package that it serves, so the `.nupkg` that a restore downloads from nuget.org is not byte for byte the file of
the release page, and does not verify. The libraries in it are the same, so run the command on
`~/.nuget/packages/yeelightclient/<version>/lib/net8.0/YeelightClient.dll` (or `net10.0`) to check the library that your application loads.
The notes of every release repeat this with its version. The attestation is also an asset of the release, `YeelightClient.<version>.intoto.jsonl`
(a Sigstore bundle, one attestation for all the files): add `--bundle YeelightClient.1.0.0-beta.1.intoto.jsonl` to the command and it checks the file
against that one, without asking GitHub for the attestation (it still fetches the keys that check the signature).
