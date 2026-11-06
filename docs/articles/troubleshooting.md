---
title: Troubleshooting
---

# Troubleshooting

The quickest way to see what is going on is to turn on [debug logging](errors-and-limits.md#logging): the client logs every message that it sends
and receives, and every search and connection. [The test app](test-app.md) shows the same traffic. What follows is organised by symptom, and
carries what was measured on real lights; where something is a guess, it says so.

## A light is missing

The Yeelight app shows a light, and `DiscoverAsync` or `StartDiscovery` does not find it. Check, in this order:

1. **LAN Control is on for that light**: in the Yeelight app, select the light, open its settings and switch on LAN Control. Without it a light
   neither answers a search nor accepts a connection.
2. **The light has power.** A light that is off at the wall answers nothing. (A light that is only off in the app answers.)
3. **The light and the computer are on the same network.** A guest or IoT network does not pass broadcasts between its clients, on many routers.
   See [It works from one network and not from another](#it-works-from-one-network-and-not-from-another).
4. **Search longer, and again.** Wait at least three seconds (the second and the third search go out a second apart), and call
   [`SearchNow`](xref:Yeelight.YeelightClient.SearchNow*) or `DiscoverAsync` again. With `StartDiscovery` running, the list fills as lights answer.
5. **Ask for its address** with [`ProbeAsync`](xref:Yeelight.YeelightClient.ProbeAsync*) (the address is in the list of clients of your router). When a
   probe finds a light that a search does not, the multicast and the broadcast of the search do not reach it: see below.
6. **Look at the firewall of the computer**: a managed Windows computer can drop the answers to a search: see
   [The Windows firewall, or a policy](#the-windows-firewall-or-a-policy).
7. **The light is listed, but does nothing**: see [A light is listed but does nothing](#a-light-is-listed-but-does-nothing).
8. **The client has as many lights as [`MaxDevices`](xref:Yeelight.YeelightClientOptions.MaxDevices)** (1000 by default; a lower number is your own
   choice). It then ignores the announcements of other lights, and writes a warning to its log once.

### What real lights do

This was measured on one home network: a Windows computer on Ethernet, and three lights on WiFi in the same /24 network (two colour bulbs, one of
them of the model `colora` with firmware 6, and a light strip of the model `strip6` with firmware 18). Every search was sent several times.

| Search | Colour bulb 1 | Light strip | Colour bulb 2 |
| --- | --- | --- | --- |
| to the multicast group `239.255.255.250:1982` (with a closing empty line or without, with `MX` or without) | answers | answers | **never answers** |
| as a broadcast, to `255.255.255.255:1982` or to the broadcast address of the network | answers | answers | answers |
| straight to the address of the light (also to all 253 addresses of the /24 in a row) | answers | answers | answers |
| another search target (`ssdp:all`, `yeelight`), or port 1900 | no | no | no |
| an announcement heard in 100 seconds | none | none | none |

The second colour bulb was healthy all along: its control service (TCP port 55443) answered `get_prop`. A client that only sent the multicast
search never saw it, and listed two lights where the Yeelight app on a phone showed three. A light answered every search twice, which does no harm:
the client keeps one device for each id.

Why the multicast did not reach that light is not known for certain. A thread on the forum of Yeelight from 2017
([forum.yeelight.com/t/yeelight-rgb-bulb-local-discovery/939](https://forum.yeelight.com/t/yeelight-rgb-bulb-local-discovery/939)) reports
the same symptom: a computer on Ethernet does not see the WiFi lights by multicast, and a computer on WiFi does. A router or an access point that
does not pass the multicast group from the wired side to every WiFi light would fit it (IGMP snooping without a querier, or the power saving of
the WiFi of the light); that was not tested. A broadcast and a search that is sent straight to the light were not affected.

What follows from it:

- The client sends both the multicast and the broadcast, three times. Turning [`UseBroadcast`](xref:Yeelight.YeelightClientOptions.UseBroadcast) off
  gives up the broadcast, and with it lights like the second colour bulb.
- Do not rely on announcements: the lights announce themselves too rarely (see [Listen](discovery.md#listen)).
- **A light that answers no search at all**, but that accepts a TCP connection, cannot be used with this library. The id, the model and the
  supported methods only come from the answer to a search (`get_prop` does not return them), and a device is made from them. None of the three
  lights above was like that: they answered a search that was sent straight to them.
- The library has no method that tries every address of a subnet. A search that was sent to every address of the /24 did find all three lights, but
  253 packets to unknown hosts look like a scan to network monitoring, and a light is not built for a crowd of connections: see
  [`client quota exceeded`](#client-quota-exceeded). Find the address in your router, and probe it.

## A light is listed but does nothing

The light is in `Devices`, and its commands fail or its state does not change.

- Look at [`IsConnected`](xref:Yeelight.YeelightDevice.IsConnected). When it is `false` there is no connection, and the commands fail with `NotConnected`:
  see [Connection refused, or only four connections](#connection-refused-or-only-four-connections).
- The commands fail with `client quota exceeded`: see [`client quota exceeded`](#client-quota-exceeded).
- The commands are refused although the light has power: it can be off in the software sense. See
  [Commands are refused when the light is off](#commands-are-refused-when-the-light-is-off).
- Look at [`LastSeen`](xref:Yeelight.YeelightDevice.LastSeen), which is when anything was last heard from the light. A light that lost its power or its WiFi
  does not close its connection, and so stays `IsConnected` until the client notices that it does not answer (a connection that is quiet for a
  minute is checked, and the client connects again): see [Commands time out](#commands-time-out).
- The state is wrong or empty: see [A light was found, but its state is empty](#a-light-was-found-but-its-state-is-empty).

## Connection refused, or only four connections

A light takes four TCP connections at the same time, and rejects a fifth. When all four are in use, the client cannot connect: `IsConnected`
stays `false`, a command fails with [`NotConnected`](xref:Yeelight.YeelightFailureKind) after waiting two seconds for a connection, and the client
tries again with a wait that grows from a second to 30 seconds. At the debug level the log says `Connection problem` and `Connecting again in ...`.

Who holds the four connections matters:

- Your own program: **one client holds one connection to each light** for as long as it runs. A second copy of your program, for example one that you
  start from the debugger while another runs, holds another one.
- The Yeelight app, Home Assistant and any other program that controls the lights over the network. A program that opens a new connection for each
  command needs a free one every time.
- A program that was closed cleanly frees its connection at once. One that crashed leaves it to the light, and how long a light keeps it was not measured.

What to do: close what you do not need; share one `YeelightClient` in your program (it can be used from several threads); and when your program only
sends a command now and then, set [`AutoConnect`](xref:Yeelight.YeelightClientOptions.AutoConnect) to `false` and call
[`DisconnectAsync`](xref:Yeelight.YeelightDevice.DisconnectAsync*) when you are done: see
[AutoConnect](discovery.md#connecting-to-the-lights-autoconnect). Another cause of a refused connection is that LAN Control is off, or that
a firewall between you and the light blocks TCP port 55443 (the port that the light says in its `Location`).

## `client quota exceeded`

A light answers `client quota exceeded` (with the error code -1) to a command when a quota is used up: 60 commands a minute on a connection, and
144 on all its connections together (see [The limits of a light](errors-and-limits.md#the-limits-of-a-light)). The command is not carried out.
The client reports it as a [`YeelightException`](xref:Yeelight.YeelightException) of the kind [`DeviceRejected`](xref:Yeelight.YeelightFailureKind),
and does not repeat the command.

**When you send many commands**, it is what it says. The client holds its own commands to 60 a minute, so it comes from the other programs that use
the same light (each has its own quota, and the light has its total), or from a `CommandsPerMinute` that you raised. Send fewer commands: lower
[`CommandsPerMinute`](xref:Yeelight.YeelightClientOptions.CommandsPerMinute), use a [colour flow](flows-and-scenes.md) instead of a series of
commands, or [music mode](music-mode.md), which has no quota.

**When you send few commands**, and a light answers it to every command, even to the first one on a new connection, the light is probably in a
state that Yeelight has confirmed as a bug of its firmware, on its forum in June 2022
([forum.yeelight.com/t/topic/15756](https://forum.yeelight.com/t/topic/15756)): the timer that refills the quota fails to restart after
2^32 milliseconds of uptime, which is about 49.7 days, and from then on every command over the LAN gets this answer. Yeelight advised to power cycle
the light from time to time, and said that a fix needed a new firmware, which it could not release at the time. A restart of the light cures it for a
while: Yeelight said for a month or so, which fits an uptime of 49.7 days, and people who reported it found that a light that was switched off at
the wall and on again worked normally. The reports of 2022 are about ceiling lights and mono bulbs, and a user of Home Assistant reported the same
for a ceiling light in [June 2026](https://github.com/home-assistant/core/issues/174665), so assume that it still happens. Whether your light has a
firmware without the bug is not known here: look for an update in the Yeelight app.

**What is not known** is what else makes a light do it. On one home network a light began to answer every command that way, the first command of a
new connection included, about a minute after a program had connected to the control port of every address of the subnet at once (and sent a UDP
search to each). The other two lights on the network took the same probe, and went on answering normally. The light kept refusing for at least nine
hours, on new connections too, while the Yeelight app on a phone still controlled it (whether through the cloud or through the LAN is not known). That
the probe started it is possible, and was not shown: nothing had asked the light anything before, so its state and its uptime before are unknown. It
is one of the reasons that this library never scans a network.

What to do, in this order:

1. Stop what may send too much: close the other programs that use the light, and check your own loops.
2. Wait a minute: a quota that is used up fills again.
3. Switch the light off at the wall for a few seconds, and on again.
4. Switching LAN Control off and on in the Yeelight app is also worth a try; whether it helps was not measured here.

## Commands time out

A command that gets no answer within [`CommandTimeout`](xref:Yeelight.YeelightClientOptions.CommandTimeout) (three seconds by default) fails with
[`Timeout`](xref:Yeelight.YeelightFailureKind). **The light may have carried the command out**, so read its state again
([`RefreshAsync`](xref:Yeelight.YeelightDevice.RefreshAsync*)) before you send a `toggle` or an adjustment again.

- The WiFi signal of the light is weak, or the light is busy: raise `CommandTimeout`, and move the light or the access point.
- The light lost its power or its WiFi. It does not close the connection when that happens, so the client only finds out when it asks: after a minute
  without a message (`KeepAliveInterval`), the client asks the light for its power, and when there is no answer it connects again. Until then, commands
  time out. `LastSeen` shows when the light was last heard.
- A command that was held back by the limit of commands fails with `RateLimited` and not with `Timeout`: see [Errors and limits](errors-and-limits.md).
- The light answered, but not in a form that is an answer: a line with an `id` and neither a `result` nor an `error` is ignored by the client, so its
  command times out. The debug log shows the line (`Received: ...`, and `Ignored a message that is not understood`): please
  [report it](https://github.com/tinodo/yeelightclient/issues) with the model and the firmware of the light.

## Commands are refused when the light is off

A light that is off in the software sense still has power and answers, but it does not accept every command: the specification names seven that
need a light that is on (brightness, colour temperature, colour, HSV, saving the default, starting a colour flow and a sleep timer), and the client
throws a `YeelightException` of the kind `DeviceRejected` when the light refuses one. Switch the light on first, or use a scene, which is accepted by
a light that is off and switches it on: see [Commands that need the light to be on](controlling.md#commands-that-need-the-light-to-be-on).

## Colour commands on a light that has no colours

A `mono` light has brightness only, and a white light that can change its colour temperature has no colours. They do not list `set_rgb` and
`set_hsv`, and the client throws a `NotSupportedException` before it sends anything. Ask first:

```csharp
if (light.SupportsColor)
{
    await light.SetRgbAsync(255, 140, 0);
}
else if (light.SupportsColorTemperature)
{
    await light.SetColorTemperatureAsync(2700);
}
else
{
    await light.SetBrightnessAsync(60);
}
```

Look at [`SupportedMethods`](xref:Yeelight.YeelightDevice.SupportedMethods), and not at the [`Model`](xref:Yeelight.YeelightDevice.Model): real lights have
models that the specification does not name. A command that you send with `SendCommandAsync` is not checked by the client, and a light that does not
know the method answers `unsupported method`.

## It works from one network and not from another

A search is multicast and broadcast UDP, and neither crosses a router. A computer on another subnet, a VPN, and a guest network do not hear the
lights, and the lights do not hear the search. The same can happen on one network: in the measurements above, a computer on Ethernet did not
reach one WiFi light by multicast, and the forum thread of 2017 says that a computer on WiFi sees lights that a computer on Ethernet does not.

- Probe the address: `await client.ProbeAsync("192.168.1.50")` works wherever a packet to that address goes. See
  [A VPN, another subnet or a guest network](discovery.md#a-vpn-another-subnet-or-a-guest-network).
- The client searches from the IPv4 addresses of the network adapters that are up and can do multicast. It leaves out loopback and tunnel
  adapters (many VPN adapters are tunnels), link-local addresses and IPv6.
- A light is reached by TCP at the address in its `Location`, so that address has to be reachable from the computer, whatever the way the light was found.

## The Windows firewall, or a policy

A computer that is managed by an organisation can have the setting **Windows Defender Firewall: Prohibit unicast response to multicast or broadcast
requests** switched on (in Group Policy, under Computer Configuration, Administrative Templates, Network, Network Connections, Windows Defender
Firewall, in the Domain Profile or the Standard Profile). It is the registry value `DisableUnicastResponsesToMulticastBroadcast`, described in
[MS-GPFAS](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpfas/ce0afeb1-d9fb-4e8c-b797-22991ebd6658), and the Firewall CSP has it as
well. The firewall then drops the answers to a multicast or a broadcast message of the computer, which are exactly the answers to a search. (Without
the setting, it lets those answers in for three seconds after the search, and drops the ones that come later.)

You can see what is in effect:

```powershell
Get-NetFirewallProfile -PolicyStore ActiveStore |
    Select-Object Name, AllowUnicastResponseToMulticast
```

`False` on the profile of your network means that the answers are dropped. A search that is sent straight to a light is not a multicast or a
broadcast request, so [`ProbeAsync`](xref:Yeelight.YeelightClient.ProbeAsync*) is not covered by the setting; that was not tested under the setting.
Ask the administrator of the computer for an exception, or probe the lights that you know.

Music mode needs the opposite: the firewall has to let in the TCP connection that the light makes to the computer: see [Music mode](music-mode.md).

## Docker, WSL and virtual machines

Multicast and broadcast packets stay on the network that they are sent on. A container on the default network of Docker, a virtual machine behind
NAT, and WSL 2 in its default (NAT) mode are on a virtual network of their own: a search from there does not reach your network, and no light can
answer it. Microsoft documents that WSL 2 has its own virtual adapter with its own address in that mode, and lists multicast support for its
[mirrored networking mode](https://learn.microsoft.com/en-us/windows/wsl/networking#mirrored-mode-networking) (Windows 11 22H2 and later). None of
these was measured with this library.

What can be done: run the program on the host, or on a network that is bridged to the LAN; or use `ProbeAsync` with the address of the light. A probe is
plain unicast UDP and the connection to the light is plain TCP, which normally work through NAT. Music mode is the exception: the light connects to the
computer, which it cannot do through NAT unless the address and the port are forwarded to the program (see `localAddress` and `port` in
[Music mode](music-mode.md#starting-music-mode)).

## A light has another address

The router gave a light another address, and its commands fail with `NotConnected` or time out for a while, or the log says `An announcement says
that it is at <new address>, but it is connected at <old address>. The announcement is ignored.`

A light that is connected is never moved by an announcement, because anyone on the network can announce the id of a light from an address of their
own. The connection to the old address of a light that got a new one is dead, but the client only finds that out when it asks: the operating system probes
a connection that is idle, and a connection that has been quiet for a minute ([`KeepAliveInterval`](xref:Yeelight.YeelightClientOptions.KeepAliveInterval))
is asked something, so this takes a minute or two. When the connection is down, the next answer to a search or announcement of the light moves it, and
the connection is made to the new address. So keep a search running ([`StartDiscovery`](xref:Yeelight.YeelightClient.StartDiscovery*), a minute by
default) or the listening on, wait a minute or two, and give the lights a fixed address in your router where you can. See
[A light is known by its id](discovery.md#a-light-is-known-by-its-id).

## Music mode does not start

[`StartMusicModeAsync`](xref:Yeelight.YeelightDevice.StartMusicModeAsync*) fails after five seconds with a
[`YeelightException`](xref:Yeelight.YeelightException) of the kind [`NotConnected`](xref:Yeelight.YeelightFailureKind) and the message `The light did
not connect to <address>:<port> for music mode. Does the firewall of this computer let it in?`. The light accepted the command, and could not make
the connection to the computer. Check:

- **The firewall of the computer** lets in a TCP connection from the light to the port that the client listens on. The port is one that the system
  chooses, unless you give `port`: give one when your firewall has to name it.
- **The address** that the client told the light is one that the light can reach. It is the address that the computer uses to talk to the light, unless
  you give `localAddress`: give one when the computer has more than one address on the network of the light, or when the light is reached through
  something that translates addresses (a container, a virtual machine, WSL in its NAT mode).
- **The light can reach the computer.** It is the light that makes the connection, so a rule between the two that lets the computer reach the light
  and not the other way round (a network for IoT devices can have one) stops it.

The client then sends `set_music` with 0 over the control connection, so that the light does not stay in music mode (it would report no changes). A
`DeviceRejected` failure is another thing: the light refused `set_music`. See [Music mode](music-mode.md).

## A light was found, but its state is empty

A property is `0` (or empty) until the light has reported it, and [`ColorMode`](xref:Yeelight.YeelightLight.ColorMode) is `Rgb` until then. A light
that was found by a search has told most of its state in its answer. The rest comes when the client has connected and read all the properties:

- With [`AutoConnect`](xref:Yeelight.YeelightClientOptions.AutoConnect) off, or [`RefreshOnConnect`](xref:Yeelight.YeelightClientOptions.RefreshOnConnect)
  off, nothing reads them. Call `await light.ConnectAsync()` and `await light.RefreshAsync()`.
- Reading starts when the connection is made and ends a moment later, so a state that you read right after the light was found can be incomplete.
  Wait for `DeviceStateChanged`, or call `RefreshAsync`.
- A light that is in music mode reports nothing.
- The name is empty when none was stored on the light: the Yeelight app keeps its own names in the cloud. See `SetNameAsync`.

## Asking for help

[Open an issue](https://github.com/tinodo/yeelightclient/issues) and tell us:

- the version of Yeelight Client, and the .NET version and the operating system,
- the `Model` and the `FirmwareVersion` of the light, and its `SupportedMethods`: lights differ,
- how the light was found (search, listening, probe), and whether the Yeelight app controls it,
- what you did, and what you expected to happen,
- the debug log of the messages around it. **Look at the log before you share it**: it holds the ids, the addresses and the names of your lights.
