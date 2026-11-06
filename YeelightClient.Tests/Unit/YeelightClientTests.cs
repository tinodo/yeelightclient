namespace Yeelight.Tests.Unit
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Xunit;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// The client as an application uses it: the list of lights that it keeps (made here from announcements, so that no network is needed),
    /// the events that tell of them, what it refuses to be asked, and how it ends what waits for a light when it is disposed.
    /// </summary>
    public class YeelightClientTests
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        private static string Message(string id, string name = "my_bulb")
        {
            return UnitMessages.Response(id: id, name: name);
        }

        [Fact]
        public void AClientThatFoundNothing_HasNoLights_AndIsNeitherSearchingNorListening()
        {
            using var client = UnitDevices.Client();

            Assert.Empty(client.Devices);
            Assert.False(client.IsSearching);
            Assert.False(client.IsListening);
            Assert.Null(client.FindDevice("0x1"));
        }

        [Fact]
        public void ANewLight_IsInTheListAndCanBeFoundById()
        {
            using var client = UnitDevices.Client();

            var device = UnitDevices.Add(client, Message("0x000000000015243f"));

            Assert.Same(device, Assert.Single(client.Devices));
            Assert.Same(device, client.FindDevice("0x000000000015243f"));
            Assert.Same(device, client.FindDevice("0X000000000015243F"));
            Assert.Null(client.FindDevice("0x000000000015243e"));
            Assert.Null(client.FindDevice(string.Empty));
        }

        [Fact]
        public void ALightThatIsAnnouncedAgain_IsTheSameObject_EvenWhenTheCaseOfItsIdDiffers()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client, Message("0x00000000000000ab"));

            var again = client.Apply(UnitMessages.Parse(Message("0x00000000000000AB", "renamed")));

            Assert.Same(device, again);
            Assert.Single(client.Devices);
            Assert.Equal("renamed", device.Name);
        }

        [Fact]
        public void TheLights_AreKeptInTheOrderInWhichTheyWereFound()
        {
            using var client = UnitDevices.Client();

            var devices = new[] { "0x3", "0x1", "0x2" }.Select(id => UnitDevices.Add(client, Message(id))).ToArray();

            Assert.Equal(devices, client.Devices);
        }

        [Fact]
        public void TheList_IsACopyThatNeverChanges_AndCannotBeChangedByWhoReadsIt()
        {
            using var client = UnitDevices.Client();
            var first = UnitDevices.Add(client, Message("0x1"));
            var before = client.Devices;

            UnitDevices.Add(client, Message("0x2"));

            Assert.Single(before);
            Assert.Equal(2, client.Devices.Count);
            Assert.IsType<ReadOnlyCollection<YeelightDevice>>(before);
            Assert.False(before is YeelightDevice[]);
            Assert.Same(first, before[0]);
        }

        [Fact]
        public async Task DeviceDiscovered_IsRaisedOnceForANewLight_WhenTheLightIsAlreadyInTheList()
        {
            using var client = UnitDevices.Client();
            var seen = new List<(YeelightDevice Device, bool Listed, bool Found)>();
            client.DeviceDiscovered += (_, args) =>
            {
                lock (seen)
                {
                    seen.Add((args.Device, client.Devices.Contains(args.Device), ReferenceEquals(client.FindDevice(args.Device.Id), args.Device)));
                }
            };

            var device = UnitDevices.Add(client, Message("0x1"));
            client.Apply(UnitMessages.Parse(Message("0x1", "again")));
            var other = UnitDevices.Add(client, Message("0x2"));
            await UnitDevices.FlushAsync(device);

            Assert.Equal([(device, true, true), (other, true, true)], seen.ToArray());
        }

        [Fact]
        public async Task DeviceDiscovered_AHandlerThatThrows_IsLogged_AndTheOthersAreStillCalled()
        {
            var logger = new ListLogger();
            using var client = UnitDevices.Client(logger: logger);
            var calls = new List<string>();
            client.DeviceDiscovered += (_, _) => calls.Add("first");
            client.DeviceDiscovered += (_, _) => throw new InvalidOperationException("The handler is broken.");
            client.DeviceDiscovered += (_, _) => calls.Add("third");

            var device = UnitDevices.Add(client);
            await UnitDevices.FlushAsync(device);

            Assert.Equal(["first", "third"], calls);
            Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException);
        }

        [Fact]
        public void TheClient_KeepsNoMoreLightsThanItsOptionsSay_AndIgnoresTheAnnouncementsOfOthers()
        {
            var logger = new ListLogger();
            using var client = UnitDevices.Client(logger: logger, maxDevices: 2);
            var first = UnitDevices.Add(client, Message("0x1"));
            var second = UnitDevices.Add(client, Message("0x2"));

            var third = client.Apply(UnitMessages.Parse(Message("0x3")));
            var fourth = client.Apply(UnitMessages.Parse(Message("0x4")));

            Assert.Null(third);
            Assert.Null(fourth);
            Assert.Equal([first, second], client.Devices);
            Assert.Null(client.FindDevice("0x3"));
            var warning = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
            Assert.Contains("already has 2 lights", warning.Message);
        }

        [Fact]
        public void TheLimitOnTheNumberOfLights_DoesNotKeepALightThatIsKnownFromBeingUpdated()
        {
            using var client = UnitDevices.Client(maxDevices: 1);
            var device = UnitDevices.Add(client, Message("0x1"));

            var again = client.Apply(UnitMessages.Parse(Message("0x1", "renamed")));

            Assert.Same(device, again);
            Assert.Equal("renamed", device.Name);
        }

        [Fact]
        public async Task ALightThatIsForgotten_MakesRoomForAnother()
        {
            using var client = UnitDevices.Client(maxDevices: 1);
            var first = UnitDevices.Add(client, Message("0x1"));
            Assert.Null(client.Apply(UnitMessages.Parse(Message("0x2"))));

            Assert.True(await client.RemoveDeviceAsync(first));
            var second = client.Apply(UnitMessages.Parse(Message("0x2")));

            Assert.NotNull(second);
            Assert.Same(second, Assert.Single(client.Devices));
        }

        [Fact]
        public async Task RemoveDeviceAsync_ForgetsTheLight_OnceOnly()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client, Message("0x1"));
            var other = UnitDevices.Add(client, Message("0x2"));

            Assert.True(await client.RemoveDeviceAsync(device));
            Assert.False(await client.RemoveDeviceAsync(device));

            Assert.Same(other, Assert.Single(client.Devices));
            Assert.Null(client.FindDevice("0x1"));
        }

        [Fact]
        public async Task ALightThatWasForgotten_IsFoundAgainAsANewObject_WithItsOwnDiscoveryEvent()
        {
            using var client = UnitDevices.Client();
            var discovered = new List<YeelightDevice>();
            client.DeviceDiscovered += (_, args) =>
            {
                lock (discovered)
                {
                    discovered.Add(args.Device);
                }
            };
            var device = UnitDevices.Add(client, Message("0x1"));
            await client.RemoveDeviceAsync(device);

            var again = UnitDevices.Add(client, Message("0x1"));
            await UnitDevices.FlushAsync(again);

            Assert.NotSame(device, again);
            Assert.Equal([device, again], discovered.ToArray());
        }

        [Fact]
        public async Task RemoveDeviceAsync_OfALightOfAnotherClient_ForgetsNothing()
        {
            using var client = UnitDevices.Client();
            using var other = UnitDevices.Client();
            var device = UnitDevices.Add(client, Message("0x1"));

            Assert.False(await other.RemoveDeviceAsync(device));

            Assert.Same(device, Assert.Single(client.Devices));
        }

        [Fact]
        public async Task RemoveDeviceAsync_ANullLight_IsRefused()
        {
            using var client = UnitDevices.Client();

            var refusal = await Assert.ThrowsAsync<ArgumentNullException>(() => client.RemoveDeviceAsync(null!));

            Assert.Equal("device", refusal.ParamName);
        }

        [Fact]
        public void FindDevice_ANullId_IsRefused()
        {
            using var client = UnitDevices.Client();

            var refusal = Assert.Throws<ArgumentNullException>(() => client.FindDevice(null!));

            Assert.Equal("id", refusal.ParamName);
        }

        [Fact]
        public void Apply_ANullAnnouncement_IsRefused()
        {
            using var client = UnitDevices.Client();

            Assert.Throws<ArgumentNullException>(() => client.Apply(null!));
        }

        [Fact]
        public void TheOptions_AreTheOnesThatTheClientWasMadeWith()
        {
            var options = new YeelightClientOptions { AutoConnect = false, MaxDevices = 7 };
            using var client = TestClients.Create(options);

            Assert.Same(options, client.Options);
        }

        [Fact]
        public async Task DisposeAsync_CalledAgainWhileTheFirstCallIsStillClosing_EndsOnlyWhenTheFirstDoes()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();

            // The discovery that listens is stuck in the function that gives it the addresses of the computer, so closing it takes a while.
            var client = TestClients.Create(
                new YeelightClientOptions { AutoConnect = false },
                adjust: options => options with
                {
                    GetLocalAddresses = () =>
                    {
                        entered.Set();
                        release.Wait();
                        return [IPAddress.Loopback];
                    },
                });
            client.StartListening();
            Assert.True(entered.Wait(Patience), "The discovery should have asked for the addresses of the computer.");

            var first = client.DisposeAsync().AsTask();
            var second = client.DisposeAsync().AsTask();
            var third = Task.Run(() => client.DisposeAsync().AsTask());

            await Wait.NotHappensAsync(() => first.IsCompleted || second.IsCompleted || third.IsCompleted, "a call of DisposeAsync that ends while the client is still closing", TimeSpan.FromMilliseconds(300));
            release.Set();
            await Task.WhenAll(first, second, third).WaitAsync(Patience);
            Assert.False(client.IsListening);
        }

        [Fact]
        public async Task ADisposedClient_HasNoLights_AndTakesNoAnnouncementsAnyMore()
        {
            var client = UnitDevices.Client();
            var device = UnitDevices.Add(client, Message("0x1"));

            await client.DisposeAsync();

            Assert.Empty(client.Devices);
            Assert.Null(client.FindDevice("0x1"));
            Assert.Null(client.Apply(UnitMessages.Parse(Message("0x2"))));
            Assert.False(await client.RemoveDeviceAsync(device));
        }

        [Fact]
        public async Task DisposingAClient_TwiceInAnyWay_IsFine()
        {
            var client = UnitDevices.Client();

            client.Dispose();
            client.Dispose();
            await client.DisposeAsync();
            await client.DisposeAsync();
        }

        [Fact]
        public async Task ADisposedClient_RefusesToLookForLights()
        {
            var client = UnitDevices.Client();
            await client.DisposeAsync();

            Assert.Throws<ObjectDisposedException>(() => client.StartDiscovery());
            Assert.Throws<ObjectDisposedException>(() => client.StartListening());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.DiscoverAsync(TimeSpan.FromSeconds(1)));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ProbeAsync(IPAddress.Loopback));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ProbeAsync("127.0.0.1"));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.WaitForDeviceAsync("0x1", TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public async Task ADisposedClient_CanStillBeAskedToStop()
        {
            var client = UnitDevices.Client();
            await client.DisposeAsync();

            client.StopDiscovery();
            client.StopListening();
            client.SearchNow();

            Assert.False(client.IsSearching);
            Assert.False(client.IsListening);
        }

        [Fact]
        public async Task WaitForDeviceAsync_ALightThatIsKnown_IsReturnedAtOnce_WhateverTheTimeout()
        {
            using var client = UnitDevices.Client();
            var device = UnitDevices.Add(client, Message("0xAbC"));

            Assert.Same(device, await client.WaitForDeviceAsync("0xabc", TimeSpan.Zero));
            Assert.Same(device, await client.WaitForDeviceAsync("0XABC", TimeSpan.FromMilliseconds(1)));
        }

        [Fact]
        public async Task WaitForDeviceAsync_WaitsUntilTheLightIsFound_WhateverTheCaseOfTheId()
        {
            using var client = UnitDevices.Client();

            var waiting = client.WaitForDeviceAsync("0X000000000000ABCD", TimeSpan.FromMinutes(5));
            Assert.False(waiting.IsCompleted);
            UnitDevices.Add(client, Message("0x1"));
            Assert.False(waiting.IsCompleted);
            var device = UnitDevices.Add(client, Message("0x000000000000abcd"));

            Assert.Same(device, await waiting.WaitAsync(Patience));
        }

        [Fact]
        public async Task WaitForDeviceAsync_ALightThatIsNotFoundInTime_IsATimeoutThatNamesTheLight()
        {
            var clock = new UnitClock();
            using var client = UnitDevices.Client(clock);

            var waiting = client.WaitForDeviceAsync("0xABC", TimeSpan.FromSeconds(5));
            Assert.False(waiting.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(4));
            Assert.False(waiting.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(1));

            var timeout = await Assert.ThrowsAsync<TimeoutException>(() => waiting);
            Assert.Contains("0xABC", timeout.Message);
            Assert.Contains("5 seconds", timeout.Message);
        }

        [Fact]
        public async Task WaitForDeviceAsync_CancelledByTheCaller_EndsWithACancellation()
        {
            var clock = new UnitClock();
            using var client = UnitDevices.Client(clock);
            using var cancellation = new CancellationTokenSource();

            var waiting = client.WaitForDeviceAsync("0x1", TimeSpan.FromHours(1), cancellation.Token);
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }

        [Fact]
        public async Task WaitForDeviceAsync_WhenTheClientIsDisposed_EndsWithACancellation()
        {
            var clock = new UnitClock();
            var client = UnitDevices.Client(clock);

            var waiting = client.WaitForDeviceAsync("0x1", TimeSpan.FromHours(1));
            await client.DisposeAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }

        [Fact]
        public async Task WaitForDeviceAsync_ANullId_IsRefused()
        {
            using var client = UnitDevices.Client();

            var refusal = await Assert.ThrowsAsync<ArgumentNullException>(() => client.WaitForDeviceAsync(null!, TimeSpan.FromSeconds(1)));

            Assert.Equal("id", refusal.ParamName);
        }

        [Fact]
        public async Task DiscoverAsync_SearchesForTheTimeThatItWaits_ThenReturnsTheLightsThatTheClientKnows()
        {
            var clock = new UnitClock();
            using var client = UnitDevices.Client(clock);
            var device = UnitDevices.Add(client, Message("0x1"));

            var discovering = client.DiscoverAsync(TimeSpan.FromSeconds(3));
            Assert.False(discovering.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(3));
            var found = await discovering.WaitAsync(Patience);

            Assert.Same(device, Assert.Single(found));
            Assert.False(client.IsSearching);
        }

        [Fact]
        public async Task DiscoverAsync_WhenTheClientSearchesAlready_AsksForASearchNowAndLeavesItGoing()
        {
            var clock = new UnitClock();
            using var client = UnitDevices.Client(clock);
            client.StartDiscovery();

            var discovering = client.DiscoverAsync(TimeSpan.FromSeconds(3));
            clock.Advance(TimeSpan.FromSeconds(3));
            await discovering.WaitAsync(Patience);

            Assert.True(client.IsSearching);
            client.StopDiscovery();
            Assert.False(client.IsSearching);
        }

        [Fact]
        public async Task DiscoverAsync_CancelledByTheCaller_EndsWithACancellation()
        {
            var clock = new UnitClock();
            using var client = UnitDevices.Client(clock);
            using var cancellation = new CancellationTokenSource();

            var discovering = client.DiscoverAsync(TimeSpan.FromHours(1), cancellation.Token);
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discovering);
            Assert.False(client.IsSearching);
        }

        [Fact]
        public async Task DiscoverAsync_WhenTheClientIsDisposed_EndsWithACancellation()
        {
            var clock = new UnitClock();
            var client = UnitDevices.Client(clock);

            var discovering = client.DiscoverAsync(TimeSpan.FromHours(1));
            await client.DisposeAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discovering);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-1000)]
        public async Task DiscoverAsync_ATimeoutThatIsNotMoreThanZero_IsRefused(int milliseconds)
        {
            using var client = UnitDevices.Client();

            var refusal = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.DiscoverAsync(TimeSpan.FromMilliseconds(milliseconds)));

            Assert.Equal("timeout", refusal.ParamName);
            Assert.False(client.IsSearching);
        }

        [Fact]
        public async Task DiscoverAsync_ATimeoutThatNoTimerCanWait_IsRefusedAsTheTimeout()
        {
            using var client = UnitDevices.Client();

            var refusal = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.DiscoverAsync(TimeSpan.FromDays(60)));

            Assert.Equal("timeout", refusal.ParamName);
            Assert.False(client.IsSearching);
        }

        [Theory]
        [InlineData(-5000)]
        [InlineData(-2)]
        public async Task WaitForDeviceAsync_ANegativeTimeout_OfALightThatIsNotKnown_IsRefused(int milliseconds)
        {
            using var client = UnitDevices.Client();

            var refusal = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.WaitForDeviceAsync("0x1", TimeSpan.FromMilliseconds(milliseconds)));

            Assert.Equal("timeout", refusal.ParamName);
        }

        [Fact]
        public async Task WaitForDeviceAsync_ATimeoutThatNoTimerCanWait_OfALightThatIsNotKnown_IsRefusedAsTheTimeout()
        {
            using var client = UnitDevices.Client();

            var refusal = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.WaitForDeviceAsync("0x1", TimeSpan.FromDays(60)));

            Assert.Equal("timeout", refusal.ParamName);
        }

        [Fact]
        public async Task WaitForDeviceAsync_AZeroTimeout_OfALightThatIsNotKnown_IsATimeoutAtOnce()
        {
            using var client = UnitDevices.Client();

            await Assert.ThrowsAsync<TimeoutException>(() => client.WaitForDeviceAsync("0x1", TimeSpan.Zero));
        }

        [Fact]
        public async Task WaitForDeviceAsync_WithNoTimeout_WaitsUntilTheLightIsFound()
        {
            using var client = UnitDevices.Client();

            var waiting = client.WaitForDeviceAsync("0x1", Timeout.InfiniteTimeSpan);
            Assert.False(waiting.IsCompleted);
            var device = UnitDevices.Add(client, Message("0x1"));

            Assert.Same(device, await waiting.WaitAsync(Patience));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(500)]
        [InlineData(999)]
        public void StartDiscovery_AnIntervalOfLessThanASecond_IsRefused(int milliseconds)
        {
            using var client = UnitDevices.Client();

            var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => client.StartDiscovery(TimeSpan.FromMilliseconds(milliseconds)));

            Assert.Equal("interval", refusal.ParamName);
            Assert.False(client.IsSearching);
        }

        [Fact]
        public void StartDiscovery_ANegativeOrAnInfiniteInterval_IsRefused()
        {
            using var client = UnitDevices.Client();

            Assert.Throws<ArgumentOutOfRangeException>(() => client.StartDiscovery(TimeSpan.FromTicks(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => client.StartDiscovery(TimeSpan.FromSeconds(-30)));
            Assert.Throws<ArgumentOutOfRangeException>(() => client.StartDiscovery(Timeout.InfiniteTimeSpan));
            Assert.False(client.IsSearching);
        }

        [Fact]
        public void StartDiscovery_AnIntervalOfMoreThan24Days_IsRefused()
        {
            using var client = UnitDevices.Client();

            Assert.Equal("interval", Assert.Throws<ArgumentOutOfRangeException>(() => client.StartDiscovery(TimeSpan.FromDays(24) + TimeSpan.FromMilliseconds(1))).ParamName);
            Assert.Throws<ArgumentOutOfRangeException>(() => client.StartDiscovery(TimeSpan.FromDays(25)));
            Assert.Throws<ArgumentOutOfRangeException>(() => client.StartDiscovery(TimeSpan.MaxValue));
            Assert.False(client.IsSearching);
        }

        [Fact]
        public void StartDiscovery_TheEdgesOfTheRange_AreAccepted()
        {
            using var client = UnitDevices.Client();

            client.StartDiscovery(TimeSpan.FromSeconds(1));
            Assert.True(client.IsSearching);
            client.StartDiscovery(TimeSpan.FromDays(24));
            Assert.True(client.IsSearching);
            client.StartDiscovery();
            Assert.True(client.IsSearching);
            client.StopDiscovery();
            Assert.False(client.IsSearching);
        }

        [Fact]
        public void StartDiscovery_AnIntervalThatIsRefused_LeavesTheSearchThatWasRunning()
        {
            using var client = UnitDevices.Client();
            client.StartDiscovery(TimeSpan.FromSeconds(5));

            Assert.Throws<ArgumentOutOfRangeException>(() => client.StartDiscovery(TimeSpan.Zero));

            Assert.True(client.IsSearching);
        }

        [Fact]
        public void TheClient_StartsAndStopsListening()
        {
            using var client = UnitDevices.Client();

            client.StartListening();
            Assert.True(client.IsListening);
            client.StartListening();
            Assert.True(client.IsListening);
            client.StopListening();
            Assert.False(client.IsListening);
            client.StopListening();
            Assert.False(client.IsListening);
        }

        [Fact]
        public void SearchNow_WithoutASearch_DoesNothing()
        {
            using var client = UnitDevices.Client();

            client.SearchNow();
            client.StopDiscovery();

            Assert.False(client.IsSearching);
        }

        [Fact]
        public async Task ProbeAsync_ANullAddress_IsRefused()
        {
            using var client = UnitDevices.Client();

            var refusal = await Assert.ThrowsAsync<ArgumentNullException>(() => client.ProbeAsync((IPAddress)null!));
            var withPort = await Assert.ThrowsAsync<ArgumentNullException>(() => client.ProbeAsync(null!, 1982));
            var text = await Assert.ThrowsAsync<ArgumentNullException>(() => client.ProbeAsync((string)null!));

            Assert.Equal("address", refusal.ParamName);
            Assert.Equal("address", withPort.ParamName);
            Assert.Equal("address", text.ParamName);
        }

        [Theory]
        [InlineData("8.8.8.8")]
        [InlineData("1.1.1.1")]
        [InlineData("0.0.0.0")]
        [InlineData("224.0.0.251")]
        [InlineData("239.255.255.250")]
        [InlineData("255.255.255.255")]
        [InlineData("::1")]
        [InlineData("fe80::1")]
        [InlineData("2001:db8::1")]
        public async Task ProbeAsync_AnAddressThatIsNotOfALocalNetwork_IsRefused_AndNothingIsSent(string address)
        {
            using var client = UnitDevices.Client();

            var refusal = await Assert.ThrowsAsync<ArgumentException>(() => client.ProbeAsync(IPAddress.Parse(address)));
            var text = await Assert.ThrowsAsync<ArgumentException>(() => client.ProbeAsync(address));

            Assert.Equal("address", refusal.ParamName);
            Assert.Equal("address", text.ParamName);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not an address")]
        [InlineData("192.168.1.256")]
        [InlineData("bulb.local")]
        [InlineData("1.2.3.4.5")]
        public async Task ProbeAsync_TextThatIsNotAnIpAddress_IsRefused(string address)
        {
            // What the framework reads as an address (192.168.1 is 192.168.0.1) is an address to probe: this only offers what it does not.
            Assert.False(IPAddress.TryParse(address.Trim(), out _), "This text is an address for the framework: the test would probe it.");
            using var client = UnitDevices.Client();

            var refusal = await Assert.ThrowsAsync<ArgumentException>(() => client.ProbeAsync(address));

            Assert.Equal("address", refusal.ParamName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(65536)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public async Task ProbeAsync_APortThatIsNotAPort_IsRefused(int port)
        {
            using var client = UnitDevices.Client();

            var refusal = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.ProbeAsync(IPAddress.Loopback, port));

            Assert.Equal("port", refusal.ParamName);
        }

        [Fact]
        public async Task ProbeAsync_WhenTheClientIsDisposed_EndsWithACancellation()
        {
            using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var port = ((IPEndPoint)silent.Client.LocalEndPoint!).Port;
            var client = UnitDevices.Client();

            var probing = client.ProbeAsync(IPAddress.Loopback, port);
            await silent.ReceiveAsync().WaitAsync(Patience);
            await client.DisposeAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probing);
        }

        [Fact]
        public async Task ProbeAsync_CancelledByTheCaller_EndsWithACancellation()
        {
            using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var port = ((IPEndPoint)silent.Client.LocalEndPoint!).Port;
            using var client = UnitDevices.Client();
            using var cancellation = new CancellationTokenSource();

            var probing = client.ProbeAsync(IPAddress.Loopback, port, cancellation.Token);
            await silent.ReceiveAsync().WaitAsync(Patience);
            await cancellation.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probing);
            Assert.Empty(client.Devices);
        }
    }
}
