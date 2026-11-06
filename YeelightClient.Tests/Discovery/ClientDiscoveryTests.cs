namespace Yeelight.Tests.Discovery
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// What a client does with its discovery: it starts and stops the searching and the listening, takes over the lights that are found,
    /// and lets go of its sockets. Every client of these tests is held to the loopback by <see cref="TestClients"/>.
    /// </summary>
    public class ClientDiscoveryTests
    {
        private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

        private static YeelightClient NewClient(SimulatedBulb? searchTarget = null, SimulatedBulb? broadcastTarget = null, int burst = 1, bool autoConnect = true)
        {
            return TestClients.Create(new YeelightClientOptions { RefreshOnConnect = false, SearchBurst = burst, AutoConnect = autoConnect }, searchTarget: searchTarget, broadcastTarget: broadcastTarget);
        }

        private static async Task<IPEndPoint> ListenEndPointAsync(YeelightClient client)
        {
            await Wait.UntilAsync(() => ClientInternals.ListenEndPoint(client) is not null, "the client to listen");
            return ClientInternals.ListenEndPoint(client)!;
        }

        [Fact]
        public async Task StartDiscovery_FindsTheLight_ConnectsToIt_AndTellsOnceEvenThoughItAnswersEveryTimeOfABurst()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = NewClient(bulb, burst: 3);
            var discovered = new EventRecorder<YeelightDevice>();
            client.DeviceDiscovered += (_, e) => discovered.Add(e.Device);

            client.StartDiscovery();

            await Wait.UntilAsync(() => client.FindDevice(bulb.Id) is { IsConnected: true }, "the light to be found and connected");
            Assert.True(client.IsSearching);
            await Wait.UntilAsync(() => bulb.SearchCount >= 3, "the three searches of the burst");
            await Wait.NotHappensAsync(() => discovered.Count > 1, "a second DeviceDiscovered for the same light");
            Assert.Same(client.FindDevice(bulb.Id), Assert.Single(discovered.Items));
            Assert.Single(client.Devices);
        }

        [Fact]
        public async Task TheSearchIsRepeatedAtTheInterval_OfStartDiscovery()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = NewClient(bulb, autoConnect: false);
            var watch = Stopwatch.StartNew();

            client.StartDiscovery(TimeSpan.FromSeconds(1));
            await Wait.UntilAsync(() => bulb.SearchCount >= 3, "three searches at an interval of a second");

            Assert.True(watch.Elapsed >= TimeSpan.FromSeconds(1.5), $"Three searches came within {watch.Elapsed.TotalSeconds:0.0} seconds: the interval is a second.");
        }

        [Fact]
        public async Task SearchNow_SendsASearchAtOnce_WhileTheDiscoveryRuns()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = NewClient(bulb, autoConnect: false);
            client.StartDiscovery(OneHour);
            await Wait.UntilAsync(() => bulb.SearchCount >= 1, "the first search");

            client.SearchNow();

            await Wait.UntilAsync(() => bulb.SearchCount >= 2, "the search that was asked for");
        }

        [Fact]
        public async Task SearchNow_WithoutADiscovery_DoesNothing()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = NewClient(bulb, autoConnect: false);

            client.SearchNow();

            await Wait.NotHappensAsync(() => bulb.SearchCount > 0, "a search from a client that does not search");
            Assert.False(client.IsSearching);
        }

        [Fact]
        public async Task StopDiscovery_StopsTheSearching_AndStartDiscoveryStartsItAgain()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = NewClient(bulb, autoConnect: false);
            client.StartDiscovery(OneHour);
            await Wait.UntilAsync(() => bulb.SearchCount >= 1, "the first search");

            client.StopDiscovery();
            Assert.False(client.IsSearching);
            client.SearchNow();
            var sent = bulb.SearchCount;
            await Wait.NotHappensAsync(() => bulb.SearchCount > sent, "a search after the discovery was stopped");

            client.StartDiscovery(OneHour);
            await Wait.UntilAsync(() => bulb.SearchCount > sent, "a search after the discovery was started again");
            Assert.True(client.IsSearching);
        }

        [Fact]
        public async Task StopDiscovery_ClosesTheSockets_AndTwiceIsFine()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = NewClient(bulb, autoConnect: false);
            client.StartDiscovery(OneHour);
            var discovery = ClientInternals.Searcher(client)!;
            await Wait.UntilAsync(() => discovery.Generations >= 1, "the sockets");
            var sockets = discovery.SearchEndPoints.ToArray();
            Assert.NotEmpty(sockets);

            client.StopDiscovery();
            client.StopDiscovery();

            await Wait.UntilAsync(() => sockets.All(PortBlocker.IsFree), "the sockets to be closed");
        }

        [Fact]
        public async Task StartingTheDiscoveryAgain_ReplacesTheRunningOne_AndClosesItsSockets()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = NewClient(bulb, autoConnect: false);
            client.StartDiscovery(OneHour);
            var first = ClientInternals.Searcher(client)!;
            await Wait.UntilAsync(() => first.Generations >= 1, "the sockets of the first discovery");
            var firstSockets = first.SearchEndPoints.ToArray();

            client.StartDiscovery(OneHour);
            var second = ClientInternals.Searcher(client)!;
            await Wait.UntilAsync(() => second.Generations >= 1, "the sockets of the second discovery");

            Assert.NotSame(first, second);
            await Wait.UntilAsync(() => firstSockets.All(PortBlocker.IsFree), "the sockets of the first discovery to be closed");
            Assert.DoesNotContain(second.SearchEndPoints, PortBlocker.IsFree);
        }

        [Fact]
        public async Task ALightThatDoesNotHearTheMulticast_IsFoundBecauseOfTheBroadcast()
        {
            await using var deaf = new SimulatedBulb(new SimulatedBulbOptions { Id = "0xDEAF" }) { AnswersSearches = false };
            await using var bulb = new SimulatedBulb();
            await using var client = NewClient(searchTarget: deaf, broadcastTarget: bulb);

            client.StartDiscovery();

            await Wait.UntilAsync(() => client.FindDevice(bulb.Id) is { IsConnected: true }, "the light to be found through the broadcast");
            Assert.True(deaf.SearchCount >= 1);
            Assert.Single(client.Devices);
        }

        [Fact]
        public async Task WithoutABroadcast_SuchALightIsNotFound_AndNothingIsSentToIt()
        {
            await using var deaf = new SimulatedBulb(new SimulatedBulbOptions { Id = "0xDEAF" }) { AnswersSearches = false };
            await using var bulb = new SimulatedBulb();
            await using var client = NewClient(searchTarget: deaf, broadcastTarget: null);

            client.StartDiscovery();
            await Wait.UntilAsync(() => deaf.SearchCount >= 1, "the search on the multicast path");

            await Wait.NotHappensAsync(() => bulb.SearchCount > 0 || client.Devices.Count > 0, "a broadcast, or a light that answers it", TimeSpan.FromMilliseconds(600));
        }

        [Fact]
        public async Task TheClientHandsItsOptionsToTheDiscoveriesThatItStarts()
        {
            var seen = new ConcurrentQueue<YeelightDiscoveryOptions>();
            var settings = new YeelightClientOptions { UseBroadcast = false, SearchBurst = 5, DiscoveryPort = 4321, AutoConnect = false };
            await using var client = new YeelightClient(
                settings,
                null,
                options =>
                {
                    seen.Enqueue(options);
                    return LoopbackDiscovery.Safe(options with
                    {
                        UseMulticast = false,
                        UseBroadcast = false,
                        BroadcastEndpoint = null,
                        SearchEndpoint = LoopbackDiscovery.DeadEndPoint,
                        ListenAddress = IPAddress.Loopback,
                        ListenPort = 0,
                        GetLocalAddresses = () => [IPAddress.Loopback],
                    });
                },
                null);

            client.StartDiscovery(TimeSpan.FromSeconds(7));
            client.StartListening();
            client.StopDiscovery();
            await client.DiscoverAsync(TimeSpan.FromMilliseconds(100));
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var probePort = FreePort.Udp();
            await client.ProbeAsync(IPAddress.Loopback, probePort, cancel.Token).ContinueWith(_ => { });

            var all = seen.ToArray();
            Assert.Equal(4, all.Length);
            var search = all[0];
            Assert.True(search.Search);
            Assert.False(search.Listen);
            Assert.Equal(TimeSpan.FromSeconds(7), search.SearchInterval);
            Assert.False(search.UseBroadcast);
            Assert.Equal(5, search.SearchBurst);
            Assert.Equal(4321, search.Port);
            var listen = all[1];
            Assert.True(listen.Listen);
            Assert.False(listen.Search);
            Assert.Equal(4321, listen.Port);
            var temporary = all[2];
            Assert.True(temporary.Search);
            Assert.Equal(TimeSpan.FromDays(1), temporary.SearchInterval);
            Assert.Equal(5, temporary.SearchBurst);
            var probe = all[3];
            Assert.Equal(4321, probe.Port);
            Assert.Equal(probePort, probe.ProbePort);
            Assert.False(probe.Search);
            Assert.False(probe.Listen);
        }

        [Fact]
        public async Task TheClientAsksForABroadcast_WhenItsOptionsSayTo()
        {
            var seen = new ConcurrentQueue<YeelightDiscoveryOptions>();
            await using var client = new YeelightClient(
                new YeelightClientOptions { UseBroadcast = true },
                null,
                options =>
                {
                    seen.Enqueue(options);
                    return LoopbackDiscovery.Safe(options with { UseMulticast = false, UseBroadcast = false, SearchEndpoint = LoopbackDiscovery.DeadEndPoint, GetLocalAddresses = () => [IPAddress.Loopback] });
                },
                null);

            client.StartDiscovery();

            Assert.True(Assert.Single(seen).UseBroadcast);
        }

        [Fact]
        public async Task DiscoverAsync_WithoutARunningDiscovery_SearchesForTheTime_AndReturnsTheLightsThatWereFound()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = NewClient(bulb, autoConnect: false);

            var found = await client.DiscoverAsync(TimeSpan.FromMilliseconds(800));

            Assert.Equal(bulb.Id, Assert.Single(found).Id);
            Assert.Same(client.FindDevice(bulb.Id), found[0]);
            Assert.False(client.IsSearching, "A discovery that DiscoverAsync started for itself ends with it.");
            Assert.True(bulb.SearchCount >= 1);
        }

        [Fact]
        public async Task DiscoverAsync_WithARunningDiscovery_AsksForASearchNow_AndTheDiscoveryGoesOn()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = NewClient(bulb, autoConnect: false);
            client.StartDiscovery(OneHour);
            await Wait.UntilAsync(() => bulb.SearchCount >= 1, "the first search");
            var searches = bulb.SearchCount;

            var found = await client.DiscoverAsync(TimeSpan.FromMilliseconds(500));

            Assert.True(bulb.SearchCount > searches, "DiscoverAsync asks the running discovery to search now.");
            Assert.Single(found);
            Assert.True(client.IsSearching);
        }

        [Fact]
        public async Task DiscoverAsync_EndsWithCancellation_WhenTheClientIsDisposedWhileItWaits()
        {
            await using var bulb = new SimulatedBulb();
            var client = NewClient(bulb, autoConnect: false);
            var discovering = client.DiscoverAsync(TimeSpan.FromMinutes(5));
            await Wait.UntilAsync(() => bulb.SearchCount >= 1, "the search");

            await client.DisposeAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discovering.WithTimeoutAsync("the discovery to end with the client", TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public async Task DiscoverAsync_StopsItsSearching_WhenItIsCancelled()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = NewClient(bulb, autoConnect: false);
            using var cancel = new CancellationTokenSource();
            var discovering = client.DiscoverAsync(TimeSpan.FromMinutes(5), cancel.Token);
            await Wait.UntilAsync(() => bulb.SearchCount >= 1, "the search");

            cancel.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discovering.WithTimeoutAsync("the discovery to end", TimeSpan.FromSeconds(5)));
            Assert.False(client.IsSearching);
        }

        [Fact]
        public async Task TheArgumentsOfTheDiscovery_AreChecked()
        {
            await using var client = NewClient();

            Assert.Throws<ArgumentOutOfRangeException>(() => client.StartDiscovery(TimeSpan.FromMilliseconds(999)));
            Assert.Throws<ArgumentOutOfRangeException>(() => client.StartDiscovery(TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => client.StartDiscovery(TimeSpan.FromSeconds(-5)));
            Assert.Throws<ArgumentOutOfRangeException>(() => client.StartDiscovery(TimeSpan.FromDays(25)));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.DiscoverAsync(TimeSpan.Zero));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.DiscoverAsync(TimeSpan.FromSeconds(-1)));
            Assert.False(client.IsSearching);

            client.StartDiscovery(TimeSpan.FromSeconds(1));
            client.StartDiscovery(TimeSpan.FromDays(24));
            Assert.True(client.IsSearching);
        }

        [Fact]
        public async Task StartListening_HearsAnAnnouncement_AndTheLightIsFoundAndConnected()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = NewClient();

            client.StartListening();
            var listener = await ListenEndPointAsync(client);
            await bulb.AnnounceAsync(listener);

            await Wait.UntilAsync(() => client.FindDevice(bulb.Id) is { IsConnected: true }, "the light to be found and connected");
            Assert.True(client.IsListening);
            Assert.False(client.IsSearching);
        }

        [Fact]
        public async Task StopListening_ClosesTheSocket_AndAnnouncementsAreNotTakenAnyMore()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = NewClient(autoConnect: false);
            client.StartListening();
            var listener = await ListenEndPointAsync(client);

            client.StopListening();
            client.StopListening();
            Assert.False(client.IsListening);
            await Wait.UntilAsync(() => PortBlocker.IsFree(listener), "the socket of the listener to be closed");
            await bulb.AnnounceAsync(listener);

            await Wait.NotHappensAsync(() => client.Devices.Count > 0, "a light that was announced to a client that does not listen");
        }

        [Fact]
        public async Task StartingToListenAgain_ListensAnew_AndTheEarlierSocketIsClosed()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = NewClient(autoConnect: false);
            client.StartListening();
            var first = await ListenEndPointAsync(client);
            var firstDiscovery = ClientInternals.Listener(client)!;

            client.StartListening();
            await Wait.UntilAsync(() => ClientInternals.Listener(client) is { ListenEndPoint: not null } current && !ReferenceEquals(current, firstDiscovery), "the new listener");
            var second = ClientInternals.ListenEndPoint(client)!;

            if (first.Port != second.Port)
            {
                await Wait.UntilAsync(() => PortBlocker.IsFree(first), "the first socket to be closed");
            }

            await Repeat.UntilAsync(() => bulb.AnnounceAsync(second), () => client.Devices.Count == 1, "the announcement on the new listener");
        }

        [Fact]
        public async Task TheListenerAndTheSearcher_AreIndependent()
        {
            await using var bulb = new SimulatedBulb();
            await using var client = NewClient(bulb, autoConnect: false);
            client.StartListening();
            client.StartDiscovery(OneHour);
            await Wait.UntilAsync(() => bulb.SearchCount >= 1, "the first search");

            client.StopListening();
            Assert.False(client.IsListening);
            Assert.True(client.IsSearching);
            client.SearchNow();
            await Wait.UntilAsync(() => bulb.SearchCount >= 2, "the search of a client that no longer listens");

            client.StartListening();
            client.StopDiscovery();
            Assert.True(client.IsListening);
            Assert.False(client.IsSearching);
        }

        [Fact]
        public async Task AClientThatListens_IgnoresJunk_AndHearsTheLight()
        {
            await using var bulb = new SimulatedBulb();
            await using var neighbour = new ScriptedResponder();
            await using var client = NewClient(autoConnect: false);
            client.StartListening();
            var listener = await ListenEndPointAsync(client);

            await neighbour.SendAsync(listener, Array.Empty<byte>());
            await neighbour.SendAsync(listener, "HTTP/1.1 404 Not Found\r\n\r\n");
            await neighbour.SendAsync(listener, new byte[5000]);
            await neighbour.SendAsync(listener, Announcements.Message("0xEVIL", "yeelight://10.9.8.7:55443", advertisement: true));
            await Repeat.UntilAsync(() => bulb.AnnounceAsync(listener), () => client.Devices.Count >= 1, "the announcement of the light after the junk");

            Assert.Equal(bulb.Id, Assert.Single(client.Devices).Id);
        }

        [Fact]
        public async Task Dispose_StopsTheSearchingAndTheListening_AndClosesTheirSockets()
        {
            await using var bulb = new SimulatedBulb();
            var client = NewClient(bulb, autoConnect: false);
            client.StartDiscovery(OneHour);
            client.StartListening();
            var searcher = ClientInternals.Searcher(client)!;
            var listener = ClientInternals.Listener(client)!;
            await Wait.UntilAsync(() => searcher.Generations >= 1 && listener.Generations >= 1, "the sockets");
            var sockets = searcher.SearchEndPoints.Append(listener.ListenEndPoint!).ToArray();

            await client.DisposeAsync();

            Assert.False(client.IsSearching);
            Assert.False(client.IsListening);
            await Wait.UntilAsync(() => sockets.All(PortBlocker.IsFree), "the sockets to be closed");
            Assert.Throws<ObjectDisposedException>(() => client.StartDiscovery());
            Assert.Throws<ObjectDisposedException>(() => client.StartListening());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.DiscoverAsync(TimeSpan.FromSeconds(1)));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ProbeAsync(IPAddress.Loopback));
            client.StopDiscovery();
            client.StopListening();
            client.SearchNow();
        }

        [Fact]
        public async Task AProbe_EndsWithCancellation_WhenTheClientIsDisposedWhileItWaits()
        {
            await using var bulb = new SimulatedBulb { AnswersSearches = false };
            var client = TestClients.Create(new YeelightClientOptions { DiscoveryPort = bulb.DiscoveryPort }, searchTarget: bulb);
            var probing = client.ProbeAsync(IPAddress.Loopback);
            await Wait.UntilAsync(() => bulb.SearchCount >= 1, "the search");

            await client.DisposeAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probing.WithTimeoutAsync("the probe to end with the client", TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public async Task StartingAndStoppingFromManyThreads_OnlyEverFailsBecauseTheClientIsDisposed_AndLeavesNoSocketOpen()
        {
            await using var bulb = new SimulatedBulb();
            var client = NewClient(bulb, autoConnect: false);
            var sockets = new ConcurrentBag<IPEndPoint>();
            var failures = new ConcurrentQueue<Exception>();
            using var stop = new CancellationTokenSource();

            void Remember()
            {
                foreach (var socket in ClientInternals.Searcher(client)?.SearchEndPoints ?? [])
                {
                    sockets.Add(socket);
                }

                if (ClientInternals.ListenEndPoint(client) is { } listener)
                {
                    sockets.Add(listener);
                }
            }

            var workers = Enumerable.Range(0, 8).Select(number => Task.Run(async () =>
            {
                var random = new Random(number);
                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        switch (random.Next(8))
                        {
                            case 0:
                                client.StartDiscovery(OneHour);
                                break;
                            case 1:
                                client.StopDiscovery();
                                break;
                            case 2:
                                client.StartListening();
                                break;
                            case 3:
                                client.StopListening();
                                break;
                            case 4:
                                client.SearchNow();
                                break;
                            case 5:
                                _ = await client.DiscoverAsync(TimeSpan.FromMilliseconds(20));
                                break;
                            case 6:
                                _ = client.IsSearching || client.IsListening;
                                break;
                            default:
                                await Task.Delay(1);
                                break;
                        }

                        Remember();
                    }
                    catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
                    {
                        // The client was disposed under the thread: that is the only failure that is allowed.
                    }
                    catch (Exception ex)
                    {
                        failures.Enqueue(ex);
                    }
                }
            })).ToArray();

            await Task.Delay(1000);
            await client.DisposeAsync();
            await Task.Delay(100);
            stop.Cancel();
            await Task.WhenAll(workers).WithTimeoutAsync("the workers to end");

            Assert.True(failures.IsEmpty, "Unexpected failure: " + failures.FirstOrDefault());
            await Wait.UntilAsync(() => sockets.All(PortBlocker.IsFree), "every socket that the client made to be closed");
        }

        [Fact]
        public async Task ADisposeRightAfterTheStart_NeverLeavesASocketOpen()
        {
            var discoveries = new List<YeelightDiscovery>();
            for (var i = 0; i < 100; i++)
            {
                var client = NewClient(autoConnect: false);
                client.StartDiscovery(OneHour);
                client.StartListening();
                discoveries.Add(ClientInternals.Searcher(client)!);
                discoveries.Add(ClientInternals.Listener(client)!);
                client.Dispose();
            }

            await Wait.UntilAsync(
                () => discoveries.All(discovery => discovery.SearchEndPoints.All(PortBlocker.IsFree) && (discovery.ListenEndPoint is null || PortBlocker.IsFree(discovery.ListenEndPoint))),
                "every socket of the discoveries that were stopped at once to be closed");
        }
    }
}
