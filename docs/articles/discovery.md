---
title: Finding lights
---

# Finding lights

A client finds a light in one of three ways, and always by what the light says about itself:

| Way | What happens | Members |
| --- | --- | --- |
| **Search** | The client sends a search to the multicast group of the lights and as a broadcast, from every network of the computer. Every light that hears it answers. | [`StartDiscovery`](xref:Yeelight.YeelightClient.StartDiscovery*), [`SearchNow`](xref:Yeelight.YeelightClient.SearchNow*), [`DiscoverAsync`](xref:Yeelight.YeelightClient.DiscoverAsync*) |
| **Listen** | A light announces itself when it joins the network, and now and then after that. The client joins the multicast group and listens. | [`StartListening`](xref:Yeelight.YeelightClient.StartListening*) |
| **Probe** | The client sends a search straight to one address. | [`ProbeAsync`](xref:Yeelight.YeelightClient.ProbeAsync*) |

Every light that is found is a [`YeelightDevice`](xref:Yeelight.YeelightDevice) in [`YeelightClient.Devices`](xref:Yeelight.YeelightClient.Devices).
A light that the client has already is not added a second time: a later answer updates the same object. The client does not search or
listen until you ask it to.

## Search

A search is a UDP message that every Yeelight light answers, sent to port 1982. The client sends it from each IPv4 address of the computer
(one socket per address), three times, a second apart, to:

- the multicast group of the lights, `239.255.255.250`, sent out of the network of that address, and
- as a broadcast: to the broadcast address of that network, and to `255.255.255.255`.

A light answers each search that it hears with a message to the address and port that the search came from. The answer holds what the light
is (its id, model, firmware and the methods that it supports) and what it is doing (on or off, brightness, colour).

Why both the multicast and the broadcast, and why three times: some lights never hear the multicast group, and do answer the broadcast; and
UDP over WiFi loses packets. On one home network, one light of three never answered the multicast search and answered every broadcast
(the measurements are in [Troubleshooting](troubleshooting.md#a-light-is-missing)). A light can answer a search more than once, which does no
harm: the client keeps one device for each id.

The networks that are searched are those of the IPv4 addresses of the network adapters that are up and can do multicast. Loopback and
tunnel adapters (many VPN adapters are tunnels) are left out, and so are link-local addresses (`169.254.x.x`) and IPv6.

```csharp
using Yeelight;

await using var client = new YeelightClient();

// Once: search for four seconds, and look at what was found.
var lights = await client.DiscoverAsync(TimeSpan.FromSeconds(4));
Console.WriteLine($"{lights.Count} lights");
```

```csharp
client.DeviceDiscovered += (_, e) => Console.WriteLine($"Found {e.Device}");

// Continuously: a search now, and one every minute.
client.StartDiscovery();
client.StartDiscovery(TimeSpan.FromSeconds(30));   // starts anew, here every 30 seconds
client.SearchNow();                                // a search now, whatever the interval says
client.StopDiscovery();
```

- [`StartDiscovery`](xref:Yeelight.YeelightClient.StartDiscovery*) sends a search at once and then every `interval`: from one second to 24
  days, and a minute when you give none. [`SearchNow`](xref:Yeelight.YeelightClient.SearchNow*) searches right away, and does nothing when no
  search is running ([`IsSearching`](xref:Yeelight.YeelightClient.IsSearching) tells).
- [`DiscoverAsync`](xref:Yeelight.YeelightClient.DiscoverAsync*) searches for the time that you give it and returns the lights that the client
  knows when that time has passed (the same list as `Devices`). When a search is running it sends a search now and waits; otherwise it searches
  for that time and stops. The lights that it found stay in `Devices` either way. Give it at least three seconds: a light answers within a
  second, but one that does not hear the multicast group is only found by the second or the third search, which go out a second apart.
- While a search or the listening runs, the client looks at the network addresses of the computer every 15 seconds. When they changed
  (WiFi comes and goes, a VPN connects, the computer wakes up) the sockets are made again, and a search that is running goes out at once. When
  the sockets cannot be made, nothing is thrown: the client writes a warning to its log and tries again after a second, then after two, and so
  on up to a minute.

## Listen

```csharp
client.StartListening();
```

[`StartListening`](xref:Yeelight.YeelightClient.StartListening*) joins the multicast group on every network of the computer and listens on UDP
port 1982 (the port is shared with other programs that listen to it). A light announces itself right after it joined the network, and after that
at a fixed interval that the announcement names (3600 seconds in the example of the specification). So listening finds a light that starts while
you listen, and it keeps the address and the state of known lights up to date, but it can take a long time to find a light that is already there:
on one home network with three lights, none was heard in 100 seconds. Use it next to a search, not instead of one.

[`StartDiscovery`](xref:Yeelight.YeelightClient.StartDiscovery*) searches only: the two are separate, and each has a `Stop` method
([`StopListening`](xref:Yeelight.YeelightClient.StopListening*), [`StopDiscovery`](xref:Yeelight.YeelightClient.StopDiscovery*)) and a property that
tells whether it runs ([`IsListening`](xref:Yeelight.YeelightClient.IsListening), [`IsSearching`](xref:Yeelight.YeelightClient.IsSearching)).

## Probe: ask one address

```csharp
YeelightDevice? light = await client.ProbeAsync("192.168.1.50");
if (light is null)
{
    Console.WriteLine("Nothing answered at 192.168.1.50.");
}
```

[`ProbeAsync`](xref:Yeelight.YeelightClient.ProbeAsync*) sends the search straight to one address, once a second for at most three seconds,
and returns the light, or `null` when nothing answered (or when the client already has [`MaxDevices`](xref:Yeelight.YeelightClientOptions.MaxDevices)
other lights). It takes the address as text or as an `IPAddress`, and optionally a UDP port other than 1982 (for a simulator). The light is in
`Devices` from then on; when the client knew it already, it gets what the answer says, which includes a new address when the connection of the light
is down (see [A light is known by its id](#a-light-is-known-by-its-id)).

The address has to be one of a local network: a private address (`10.x.x.x`, `172.16.x.x` to `172.31.x.x`, `192.168.x.x`), a link-local or a
loopback address, or one of `100.64.0.0/10`, the shared range that some VPNs use. Any other address, and an IPv6 address, is refused with an
`ArgumentException` before anything is sent. A probe is plain unicast UDP, so it works wherever a packet to that address goes: see
[A VPN, another subnet or a guest network](#a-vpn-another-subnet-or-a-guest-network).

## The lights that were found

```csharp
foreach (var device in client.Devices)
{
    Console.WriteLine(device);
}

var known = client.FindDevice("0x000000000015243f");   // null when the client has not found it

client.StartDiscovery();

// Throws a TimeoutException when the light is not found in time.
var lamp = await client.WaitForDeviceAsync("0x000000000015243f", TimeSpan.FromSeconds(10));

await client.RemoveDeviceAsync(lamp);   // forget it, and close its connection
```

- [`Devices`](xref:Yeelight.YeelightClient.Devices) is a read-only copy that never changes, so you can loop over it freely. Ask again, or use
  [`DeviceDiscovered`](xref:Yeelight.YeelightClient.DeviceDiscovered), to learn about the lights that are found later.
- [`DeviceDiscovered`](xref:Yeelight.YeelightClient.DeviceDiscovered) is raised once for each light, when it is found for the first time. The
  light is in `Devices` by then, and the client is connecting to it. It is raised by the dispatcher of the client: see
  [Events and state](events.md).
- [`FindDevice`](xref:Yeelight.YeelightClient.FindDevice*) looks a light up by its id (the case does not matter).
- [`WaitForDeviceAsync`](xref:Yeelight.YeelightClient.WaitForDeviceAsync*) returns the light at once when the client knows it, and otherwise
  waits for it. It does not look for the light: start a search, the listening or a probe first. It throws a `TimeoutException` when the
  light was not found in time.
- [`RemoveDeviceAsync`](xref:Yeelight.YeelightClient.RemoveDeviceAsync*) forgets a light and closes its connection. It returns `false` when
  the client did not have the light. The light is found again, as a new object, when it announces itself or answers a search again.
- A client keeps at most [`MaxDevices`](xref:Yeelight.YeelightClientOptions.MaxDevices) lights, 1000 by default: anyone on the network can
  announce lights, and what the client keeps is bounded. When it has that many, the announcements of other lights are ignored, and a warning is
  logged once. A client for a home can set a lower number.
- Disposing the client ends a [`DiscoverAsync`](xref:Yeelight.YeelightClient.DiscoverAsync*), a [`ProbeAsync`](xref:Yeelight.YeelightClient.ProbeAsync*)
  or a [`WaitForDeviceAsync`](xref:Yeelight.YeelightClient.WaitForDeviceAsync*) that is still waiting with an `OperationCanceledException`.

## A light is known by its id

A light has an [`Id`](xref:Yeelight.YeelightDevice.Id), for example `0x000000000015243f`, and that is the only thing that names it. Its name
can be changed ([`SetNameAsync`](xref:Yeelight.YeelightDevice.SetNameAsync*), the Yeelight app) and a router can give it another address. Keep
the id, not the address or the name, when you remember a light:

```csharp
const string lampId = "0x000000000015243f";   // saved from an earlier run

client.StartDiscovery();
var lamp = await client.WaitForDeviceAsync(lampId, TimeSpan.FromSeconds(10));
await lamp.TurnOnAsync();
```

**A light that got another address is followed.** When the router gives a light another address, its connection to the old one breaks, and the
client notices within a minute or two (the operating system probes a connection that is idle, and the client asks a connection that has been quiet for a
minute: see [Errors and limits](errors-and-limits.md#the-limits-of-a-light)).
The next answer or announcement of that light then moves it: the device takes the new address (its
[`Location`](xref:Yeelight.YeelightDevice.Location), [`Address`](xref:Yeelight.YeelightDevice.Address) and
[`Port`](xref:Yeelight.YeelightDevice.Port) change, and `PropertyChanged` says so) and the connection is made to the new address at once. The answer
to a probe of the new address does the same. So keep a search running (the default of a minute is enough) or the listening on when the addresses
of your lights can change.

**A light that is connected is not moved.** An announcement that puts a light that has a working connection at another host or port is ignored
entirely, its state included, and a warning is logged once. A light does not have two addresses, a connection that works says where it is, and anyone
on the network can announce the id of a light from an address of their own, which would take the light over.

## Connecting to the lights: AutoConnect

By default the client connects to every light as soon as it is found, and keeps the connection up. That is what keeps the properties of a
light up to date: a light reports every change to the connections that it has. A light takes at most four connections at the same time
([`YeelightLimits.ConnectionsPerLight`](xref:Yeelight.YeelightLimits.ConnectionsPerLight)) and rejects a fifth, and the apps and the other
programs that control the same light need theirs. One client uses one connection for each light.

A program that only sends a command now and then can leave the connection out:

```csharp
var options = new YeelightClientOptions { AutoConnect = false };
await using var client = new YeelightClient(options);
```

A light then connects when it gets a command, or when you call [`ConnectAsync`](xref:Yeelight.YeelightDevice.ConnectAsync*), and
[`DisconnectAsync`](xref:Yeelight.YeelightDevice.DisconnectAsync*) frees the connection again. The price: without a connection the state of
a device only changes when the light answers a search or announces itself, which is not when something happens to the light; and the first
command after a pause has to make the connection first. See [Errors and limits](errors-and-limits.md#the-limits-of-a-light) for the limits.

## A VPN, another subnet or a guest network

A search is multicast and broadcast UDP, and neither crosses a router. A light on another subnet, behind a VPN, or on a network that does not
pass multicast and broadcast between its clients (a guest or an IoT network, on many routers) is not found by a search, and not by listening
either. A light answers a search that is sent straight to it, wherever it is, as long as the address can be reached. So when you know the address
(from the list of clients of your router, for example), ask for it:

```csharp
var light = await client.ProbeAsync("10.8.0.23");
```

The address of a light can change when the router gives it another one; give the lights a fixed address in the router where you can. Network
adapters that report themselves as tunnels, which many VPN adapters do, are not searched at all.

## Options

These are options of [`YeelightClientOptions`](xref:Yeelight.YeelightClientOptions), which the client reads once, when it is made.

| Option | Default | What it does |
| --- | --- | --- |
| [`UseBroadcast`](xref:Yeelight.YeelightClientOptions.UseBroadcast) | `true` | Also send a search as a broadcast, next to the multicast. Some lights never hear the multicast group (an access point or a router that does not pass it on to every WiFi client) and do answer the broadcast. Turn it off for a network where a broadcast is not wanted; the search then only finds lights that hear the multicast. |
| [`SearchBurst`](xref:Yeelight.YeelightClientOptions.SearchBurst) | `3` | How many times a search is sent when it is due, a second apart. UDP over WiFi loses packets, and a light that saves power can miss one. |
| [`DiscoveryPort`](xref:Yeelight.YeelightClientOptions.DiscoveryPort) | `1982` | The UDP port that lights are searched on and that they announce themselves to. A light uses 1982: another port is for a simulator or a forwarded port. |
| [`AutoConnect`](xref:Yeelight.YeelightClientOptions.AutoConnect) | `true` | Connect to a light as soon as it is found. See above. |
| [`RefreshOnConnect`](xref:Yeelight.YeelightClientOptions.RefreshOnConnect) | `true` | Read all the properties of a light each time that a connection to it is made. See [Events and state](events.md). |
| [`MaxDevices`](xref:Yeelight.YeelightClientOptions.MaxDevices) | `1000` | The most lights that the client keeps (at least 1). When it has that many, it ignores the announcements of other lights, and logs a warning once. |

## What the client sends

- **Searching** sends UDP datagrams of 86 bytes. For each IPv4 address of the computer and for each time that a search is sent, there is one to
  the multicast group and up to two broadcasts, so a search is at most nine datagrams per address (three with `UseBroadcast` off), spread over
  two seconds.
- **Listening** sends no message of its own: it joins the multicast group (which makes the operating system announce the membership to the
  network, as it does for any program that joins a group) and receives.
- **Probing** sends one datagram a second to the address that you gave, for at most three seconds.
- **Connecting** opens one TCP connection to the address and the port that a light gave in its `Location`, for each light that the client
  found (unless `AutoConnect` is off), and the client sends commands over it. When the client puts a light in music mode, it also listens for
  the connection of the light: see [Music mode](music-mode.md).

The client sends nothing to an address that no light announced itself from, apart from the address that you give to `ProbeAsync`. **It never
scans a network**, and it has no method that would: a search or a connection to every address of a subnet looks like a port scan to network
monitoring, and lights are not built for it (see `client quota exceeded` in [Troubleshooting](troubleshooting.md#client-quota-exceeded)).

## What the client believes

The protocol has no authentication and no encryption: any computer on the network can control a light, and any computer can announce one.
Anyone can send a datagram to the port that the client listens on, or to the port that it searched from. The client goes as far as a client can
go:

- It believes an announcement, or an answer to a search, only when the message is at most 4096 bytes, starts with `HTTP/1.1 200` (an answer) or
  `NOTIFY * HTTP/1.1` (an announcement, which has to be of the kind `ssdp:alive`), and has a `Location` of the form `yeelight://address:port` and
  an `id` of at most 64 characters.
- **The address in the `Location` has to be the address that the message came from**, and that address has to be on a local network (the ranges
  that are named under [Probe](#probe-ask-one-address)). So a message cannot make the client connect to another computer than the one that sent
  it, and not to an address outside a local network.
- **A light that is connected is never moved** to another address by an announcement (see [A light is known by its id](#a-light-is-known-by-its-id)).
  A light whose connection is down is moved by the next announcement.
- **The client keeps at most `MaxDevices` lights.**

A message that fails any of this is dropped, and changes nothing in the client.

> [!NOTE]
> These checks keep the client from being pointed elsewhere; they do not make a light private. With this library or without it, any computer on
> the same network can search for the lights, connect to them and control them. LAN Control is the only switch: a light that has it off does not
> take these connections. Use the library on a network that you trust, and give a light that must not be found by anyone else a network of its own.
