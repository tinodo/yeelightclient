---
title: Errors and limits
---

# Errors and limits

## How a command fails

A command returns when the light has answered. It can end in one of these ways:

| What you get | Why | Was anything sent? |
| --- | --- | --- |
| `ArgumentOutOfRangeException`, `ArgumentException`, `ArgumentNullException` | An argument that a light would refuse (a brightness of 101), or that cannot be combined with another. | No |
| `NotSupportedException` | The light does not list the method among those that it supports. | No |
| [`YeelightException`](xref:Yeelight.YeelightException) | The light refused the command, or it did not get to the light, or the answer did not come back. [`Kind`](xref:Yeelight.YeelightException.Kind) says which. | Depends on the kind: see [below](#the-kinds-of-failure) |
| `OperationCanceledException` | The `CancellationToken` that you passed was cancelled. | Not always: see [Cancellation](#cancellation) |

Three more things come from other members, not from commands: an `ObjectDisposedException` when you start a search, a probe or a wait on a client that
is disposed, an `OperationCanceledException` for a [`DiscoverAsync`](xref:Yeelight.YeelightClient.DiscoverAsync*),
[`ProbeAsync`](xref:Yeelight.YeelightClient.ProbeAsync*) or [`WaitForDeviceAsync`](xref:Yeelight.YeelightClient.WaitForDeviceAsync*) that is still
waiting when the client is disposed, and a `TimeoutException` from `WaitForDeviceAsync` when the light was not found in time.

```csharp
try
{
    await light.SetBrightnessAsync(50);
}
catch (YeelightException ex) when (ex.Kind == YeelightFailureKind.DeviceRejected)
{
    // For example, the light is off.
    Console.WriteLine($"The light refused {ex.Method}: {ex.Error?.Message}");
}
catch (YeelightException ex) when (ex.Kind is YeelightFailureKind.Timeout
                                            or YeelightFailureKind.ConnectionLost)
{
    Console.WriteLine($"No answer, so it is not known whether the light did it: {ex.Message}");
}
catch (YeelightException ex)
{
    Console.WriteLine($"It did not get through: {ex.Message}");
}
catch (NotSupportedException)
{
    Console.WriteLine("This light cannot do that.");
}
```

## YeelightException

The client and the connection are fine after a [`YeelightException`](xref:Yeelight.YeelightException): a failure of one command does not end the
connection, and a connection that was lost is made again by the client. It has:

- [`Kind`](xref:Yeelight.YeelightException.Kind), a [`YeelightFailureKind`](xref:Yeelight.YeelightFailureKind), which says why the command did not
  succeed (below).
- [`Method`](xref:Yeelight.YeelightException.Method), the method of the protocol of the command that failed, for example `set_power`.
- [`Error`](xref:Yeelight.YeelightException.Error), a [`YeelightError`](xref:Yeelight.YeelightError) with the [`Code`](xref:Yeelight.YeelightError.Code)
  and the [`Message`](xref:Yeelight.YeelightError.Message) of the failure. A light answers `-1` to everything that it refuses; the codes from
  `-100` down are the client's own, for what went wrong on the way to the light, so look at `Kind` and not at the number.

The message of the exception for a refusal is `The light refused 'set_bright': <the message of the light> (code -1).`, and for the other kinds the
message of the client.

### The kinds of failure

| `Kind` | What happened | What to do |
| --- | --- | --- |
| [`DeviceRejected`](xref:Yeelight.YeelightFailureKind) | The light received the command and answered with an error: an unsupported method, parameters that it does not accept, a command that needs a light that is on, or a quota that is used up (`client quota exceeded`). The command was not carried out. | Read `Error.Message`. Check that the light is on, that it supports the command and that your values are in the range of the model. For the quota: see [The limits of a light](#the-limits-of-a-light). |
| [`NotConnected`](xref:Yeelight.YeelightFailureKind) | There is no connection to the light, and none was made in time. The command waits up to two seconds for a connection that is being made. Nothing was sent. | The light is not reachable, LAN Control is off, all four connections of the light are in use, the light has another address that the client has not heard yet, or the light was forgotten by the client. The client keeps trying. Try again later, or wait with [`ConnectAsync`](xref:Yeelight.YeelightDevice.ConnectAsync*). See [Troubleshooting](troubleshooting.md#connection-refused-or-only-four-connections). |
| [`Timeout`](xref:Yeelight.YeelightFailureKind) | The light did not answer within [`CommandTimeout`](xref:Yeelight.YeelightClientOptions.CommandTimeout), or it answered with a line that is not an answer (a line with an `id`, and neither a `result` nor an `error`), which the client ignores. **The command may have been carried out.** | Read the state again ([`RefreshAsync`](xref:Yeelight.YeelightDevice.RefreshAsync*)) before you send the command again, and especially before you repeat a `toggle` or an adjustment. See [Troubleshooting](troubleshooting.md#commands-time-out). |
| [`RateLimited`](xref:Yeelight.YeelightFailureKind) | The client held the command back, because it would have gone over the number of commands a minute that a light accepts, and the wait for its turn would have been longer than two seconds. Nothing was sent. | Send fewer commands: see [The limits of a light](#the-limits-of-a-light). For effects, use a [colour flow](flows-and-scenes.md) or [music mode](music-mode.md). |
| [`ConnectionLost`](xref:Yeelight.YeelightFailureKind) | The connection to the light was lost while the command waited for its answer. **The command may have been carried out.** | As for `Timeout`. The client connects again by itself. |

A command that sets a value (a brightness, a colour) can be repeated safely when you do not know whether it arrived. A command that changes
relative to what the light shows (`ToggleAsync`, the adjustments) cannot: read the state first.

## YeelightResult: failures without an exception

[`SendCommandAsync`](xref:Yeelight.YeelightLight.SendCommandAsync*) does not throw for a command that did not succeed. It returns a
[`YeelightResult`](xref:Yeelight.YeelightResult) with the answer of the light, or with the failure that took its place:

```csharp
var result = await light.SendCommandAsync("set_power", ["on", "smooth", 500]);

if (!result.Success)
{
    Console.WriteLine($"{result.Error!.Kind}: {result.Error.Message}");
}

// Throws the YeelightException that a typed command would have thrown.
result.EnsureSuccess("set_power");
```

[`Success`](xref:Yeelight.YeelightResult.Success) is `true` when the light did not answer with an error. [`Error`](xref:Yeelight.YeelightResult.Error) is
the [`YeelightError`](xref:Yeelight.YeelightError) otherwise, with its [`Kind`](xref:Yeelight.YeelightError.Kind) and
[`IsTransportFailure`](xref:Yeelight.YeelightError.IsTransportFailure) (the way to the light failed, as opposed to the light refusing).
[`Values`](xref:Yeelight.YeelightResult.Values) are the values of the answer (`"ok"` for a command that was carried out, the values of the properties
for `get_prop`), and [`GetString`](xref:Yeelight.YeelightResult.GetString*) reads one as text. It still throws for an argument that cannot be sent (a
method that is empty, a parameter that is not a number, text or a truth value) and when you cancel it.

## Cancellation

Every command, [`ConnectAsync`](xref:Yeelight.YeelightDevice.ConnectAsync*), [`ProbeAsync`](xref:Yeelight.YeelightClient.ProbeAsync*),
[`DiscoverAsync`](xref:Yeelight.YeelightClient.DiscoverAsync*) and [`WaitForDeviceAsync`](xref:Yeelight.YeelightClient.WaitForDeviceAsync*) take a
`CancellationToken`. Cancelling it ends the wait with an `OperationCanceledException`, and leaves the connection usable. A command that was already
sent is **not undone**: the light may still carry it out, and its answer is ignored when it arrives. A command that fails because of its own
timeout is not a cancellation: it throws a `YeelightException` of the kind `Timeout`. Disposing the client cancels a `DiscoverAsync`, a
`ProbeAsync` and a `WaitForDeviceAsync` that are still waiting, in the same way.

```csharp
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

try
{
    var found = await client.ProbeAsync("192.168.1.50", cancellation.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Stopped waiting.");
}
```

## Timeouts

| Option | Default | What it limits |
| --- | --- | --- |
| [`CommandTimeout`](xref:Yeelight.YeelightClientOptions.CommandTimeout) | 3 seconds | How long a command waits for the answer of the light, after it was sent. |
| [`ConnectTimeout`](xref:Yeelight.YeelightClientOptions.ConnectTimeout) | 3 seconds | How long the client waits for a connection to be made before it gives up and tries again, and how long `ConnectAsync` waits. |
| [`KeepAliveInterval`](xref:Yeelight.YeelightClientOptions.KeepAliveInterval) | 60 seconds | How long a connection may be quiet before the client asks the light something, to find out whether it is still there. `TimeSpan.Zero` turns it off. |

Two more waits are fixed. A command waits up to two seconds for a connection that is being made (after a loss, or at the start), and up to two
seconds for its turn under the limit of commands. Both end at once, with [`NotConnected`](xref:Yeelight.YeelightFailureKind), when the client is
disposed, or the light is disconnected or forgotten: a command is never kept waiting for a connection that is being closed. The options are read
when the client is made:

```csharp
var options = new YeelightClientOptions
{
    CommandTimeout = TimeSpan.FromSeconds(5),
    CommandsPerMinute = 40,
};

await using var client = new YeelightClient(options);
```

A value that cannot be used throws an `ArgumentOutOfRangeException` when the client is made: a `CommandTimeout` or a `ConnectTimeout` of zero or less,
a `KeepAliveInterval` below zero, any of these three longer than about 49 days, a
[`CommandsPerMinute`](xref:Yeelight.YeelightClientOptions.CommandsPerMinute), a [`SearchBurst`](xref:Yeelight.YeelightClientOptions.SearchBurst) or a
[`MaxDevices`](xref:Yeelight.YeelightClientOptions.MaxDevices) below 1, and a [`DiscoveryPort`](xref:Yeelight.YeelightClientOptions.DiscoveryPort) outside
1 to 65535.

## The limits of a light

The specification gives these limits, and the client keeps within them:

- A light takes **four connections** at the same time, and rejects a fifth
  ([`YeelightLimits.ConnectionsPerLight`](xref:Yeelight.YeelightLimits.ConnectionsPerLight)).
- A connection has a quota of **60 commands a minute** ([`YeelightLimits.CommandsPerMinute`](xref:Yeelight.YeelightLimits.CommandsPerMinute)).
- The light has a total quota for all its connections of **144 commands a minute** (the specification gives it as 4 × 60 × 60%).
- In [music mode](music-mode.md) no quota is checked.

A light that is over a quota answers `client quota exceeded`, which the client reports as a `DeviceRejected` failure with that message. These limits
are the word of the specification (and of Yeelight's forum); the client has no way to see how a light counts, so what follows is what the client does,
not a promise about what a light accepts.

**How the client keeps within them:**

- **One connection for each light**, which leaves three for the app of Yeelight and the other programs. A program that only sends a command now and then
  can hold none between commands: see [AutoConnect](discovery.md#connecting-to-the-lights-autoconnect).
- **A limit on the pace of the commands** of that connection: a token bucket. After a quiet moment up to 10 commands go through at once. After that
  one command is let through for each token that comes in, and the tokens come in at the pace that is left of
  [`CommandsPerMinute`](xref:Yeelight.YeelightClientOptions.CommandsPerMinute) when the burst of 10 is taken off it: 50 a minute by default, one every
  1.2 seconds. A command that has to wait gets its turn in the order in which it asked. A loop that awaits
  every command is slowed down to the pace once its burst is used up. Commands that are started together are held back in the same way, and a
  command that would have to wait more than two seconds fails at once with [`RateLimited`](xref:Yeelight.YeelightFailureKind), and nothing is
  sent. (Measured against a simulated light on the loopback: of 20 commands that were started together on a new connection, 10 went at once, one
  after 1.2 seconds, and 9 failed at once with `RateLimited`; a loop that awaited every command did 10 at once, and then one every 1.2 seconds.)
- **The keep-alive costs a command.** When a connection has been quiet for a minute, the client asks the light for its power, and that goes through
  the same limit.
- **Only its own connection is counted.** The other programs on the same light have their own quota on their own connection, but the light
  has its total of 144. When other programs are busy on the same light, lower `CommandsPerMinute`.

The burst is a part of the quota and not on top of it: the burst and the pace together are `CommandsPerMinute`, so no minute, however it is cut, holds
more commands than that. The specification does not say how a light counts a minute (as a sliding or a fixed window), and this holds for both. A
lower `CommandsPerMinute` is the safe choice when you see the quota error.

### client quota exceeded

The answer of a light to a command, when a quota is used up. A command that got it was not carried out. The client does not treat it specially,
and does not repeat the command: the exception has the kind `DeviceRejected` and the message `client quota exceeded`.

```csharp
try
{
    await light.SetBrightnessAsync(50);
}
catch (YeelightException ex) when (ex.Error?.Message == "client quota exceeded")
{
    // Send fewer commands, or use a colour flow or music mode.
}
```

Some lights keep answering it to every command, although commands are rare. That is a bug of the firmware, which Yeelight confirmed on its
forum, and no quota of yours: see [`client quota exceeded` in Troubleshooting](troubleshooting.md#client-quota-exceeded).

## Logging

The client writes to the `ILogger<YeelightClient>` that you give it, and writes nothing without one. A logger that throws does not disturb the
client: the exception is caught and the message is lost. The message of a light is the `{Light}` that starts most lines: its id.

| Level | What is logged |
| --- | --- |
| Debug | Every message that is sent to a light (`Sending: ...`) and every message that is received (`Received: ...`). A message that is not understood (a line that is not an answer is ignored with this message, and its command ends with `Timeout`). A connection that ended, a problem with it and the wait before the next attempt. A search that could not be sent from an address, and an address that could not be used. What could not be read after connecting, and music mode that could not be switched off on the control connection. |
| Information | A light that was found, a connection that was made and one that ended, a light that stopped answering the keep-alive, the network addresses that changed (the sockets of the search are made again), and music mode that is on. |
| Warning | A search or the listening that failed (it is tried again) or took too long to stop, a message that could not be handled, network addresses that are not known, a location that cannot be used, an announcement that puts a connected light at another address (it is ignored, and this is logged once for each address), a state report or an answer that could not be taken over, a read of the properties after connecting that failed in an unexpected way, 1000 notifications that are waiting, and a client that has [`MaxDevices`](xref:Yeelight.YeelightClientOptions.MaxDevices) lights. |
| Error | A handler of an event that threw an exception, a notification that could not be delivered, and a dispatcher that stopped. |

To see what goes over the wire, turn on the debug level. `AddConsole` is in the `Microsoft.Extensions.Logging.Console` package (`dotnet add package
Microsoft.Extensions.Logging.Console`); the client itself only depends on `Microsoft.Extensions.Logging.Abstractions`, for the `ILogger` interface.

```csharp
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(builder => builder
    .AddConsole()
    .SetMinimumLevel(LogLevel.Debug));

var logger = loggerFactory.CreateLogger<YeelightClient>();
await using var client = new YeelightClient(options: null, logger);
```

With dependency injection, register the client with a factory, so that the container gives it its logger (the constructor takes the options as
well, and `null` stands for the defaults):

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

services.AddSingleton(provider => new YeelightClient(
    new YeelightClientOptions(),
    provider.GetRequiredService<ILogger<YeelightClient>>()));
```

[The test app](test-app.md) shows the same traffic on its Log page.
