namespace Yeelight.Tests.Fuzz
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;
    using FsCheck;
    using FsCheck.Fluent;
    using Xunit.Abstractions;
    using Xunit.Sdk;
    using Yeelight.Tests.TestSupport;
    using static Yeelight.Tests.Fuzz.FuzzAssertions;

    /// <summary>
    /// Fuzz tests of what arrives on the UDP port of the lights, where anyone on the network can send: whatever it is, the discovery takes
    /// what the readers of the messages take and nothing else, goes on, and what the client makes of an announcement keeps the state of its
    /// light sound.
    /// </summary>
    [Trait("Category", "Fuzz")]
    public class DatagramFuzzTests(ITestOutputHelper output)
    {
        private const string SentinelPrefix = "0xSENTINEL";

        // The most that the discovery reads of one datagram: the messages of the lights are a few hundred bytes.
        private const int MaxDatagramLength = 4096;

        private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

        [Fact]
        public async Task WhateverArrivesOnThePort_OnlyWhatTheReadersTakeIsAnnounced_AndTheDiscoveryGoesOn()
        {
            var datagrams = FuzzGenerators.Datagram().Sample(FuzzSettings.Cases, new Rnd(FuzzSettings.Seed + 200), 12).ToList();
            await using var discovery = LoopbackDiscovery.Create(LoopbackDiscovery.Listen());
            var heard = new List<DeviceAnnouncement>();
            var sentinels = new ConcurrentDictionary<string, TaskCompletionSource>();
            discovery.Announced += announcement =>
            {
                if (announcement.Id.StartsWith(SentinelPrefix, StringComparison.Ordinal))
                {
                    if (sentinels.TryGetValue(announcement.Id, out var waiting))
                    {
                        waiting.TrySetResult();
                    }

                    return;
                }

                lock (heard)
                {
                    heard.Add(announcement);
                }
            };
            discovery.Start();
            await Wait.UntilAsync(() => discovery.ListenEndPoint is not null, "the listener");
            var listener = discovery.ListenEndPoint!;
            await using var sender = new ScriptedResponder();
            int accepted = 0, dropped = 0;

            for (var index = 0; index < datagrams.Count; index++)
            {
                var bytes = datagrams[index];
                var expected = Expected(bytes, IPAddress.Loopback);
                string Described() => $"datagram {index + 1} of {datagrams.Count} ({FuzzSettings.Description}), {bytes.Length} bytes: {FuzzGenerators.Preview(Encoding.UTF8.GetString(bytes))}";

                int before;
                lock (heard)
                {
                    before = heard.Count;
                }

                List<DeviceAnnouncement> got;
                var attempt = 0;
                while (true)
                {
                    await sender.SendAsync(listener, bytes);
                    await HearSentinelAsync(sender, listener, sentinels, $"{SentinelPrefix}{index:x8}{attempt:x2}", Described);
                    lock (heard)
                    {
                        got = heard.Skip(before).ToList();
                    }

                    // A datagram that is taken can be lost on the way (that is UDP): it is sent again, a few times. One that is dropped is not waited for.
                    if (expected is null || got.Count > 0 || ++attempt > 5)
                    {
                        break;
                    }
                }

                if (expected is null)
                {
                    Assert.True(got.Count == 0, $"{got.Count} announcements were taken of {Described()}, and the readers do not take it: {(got.Count > 0 ? got[0] : null)}");
                    dropped++;
                }
                else
                {
                    Assert.True(got.Count > 0, $"Nothing was announced of {Described()}, and the readers take it.");
                    Assert.All(got, announcement => SameAnnouncement(expected, announcement, Described));
                    accepted++;
                }
            }

            Assert.Equal(1, discovery.Generations);
            output.WriteLine($"{datagrams.Count} datagrams ({FuzzSettings.Description}): {accepted} were announcements, {dropped} were dropped.");
        }

        [Fact]
        public async Task WhateverAnnouncementsSay_TheStateOfTheLightsStaysSound_AndTheClientKeepsItsBounds()
        {
            var texts = FuzzGenerators.Announcement().Sample(FuzzSettings.Cases, new Rnd(FuzzSettings.Seed + 201), 12).ToList();
            const int Maximum = 25;
            string[] ids = ["0xA", "0xb", "0xC", "0xd", "0xE"];

            // Nothing is connected: the addresses that the announcements name are not for the tests to call.
            await using var client = TestClients.Create(new YeelightClientOptions { AutoConnect = false, MaxDevices = Maximum });
            var applied = 0;

            for (var index = 0; index < texts.Count; index++)
            {
                if (!DeviceAnnouncement.TryParse(texts[index], out var announcement) || !announcement!.IsFrom(IPAddress.Loopback))
                {
                    continue;
                }

                // A few ids, so that most announcements are about a light that is known already.
                announcement = announcement with { Id = ids[index % ids.Length] };
                string Described() => $"announcement {index + 1} of {texts.Count} ({FuzzSettings.Description}): {FuzzGenerators.Preview(texts[index])}";

                var device = client.Apply(announcement);

                Assert.NotNull(device);
                Assert.Equal(announcement.Id, device.Id, ignoreCase: true);
                Assert.True(client.Devices.Count <= Maximum, "The client holds more lights than it was told to.");
                AssertSound(device, Described);
                AssertSound(device.Background, Described);
                applied++;
            }

            Assert.InRange(client.Devices.Count, 1, ids.Length);
            output.WriteLine($"{texts.Count} announcements ({FuzzSettings.Description}): {applied} were applied to {client.Devices.Count} lights.");
        }

        [Fact]
        public async Task WhateverAnnouncementsSay_ALightThatIsKnown_IsOneObject_AndKeepsItsId()
        {
            var texts = FuzzGenerators.Announcement().Sample(FuzzSettings.Cases, new Rnd(FuzzSettings.Seed + 202), 12).ToList();
            await using var client = TestClients.Create(new YeelightClientOptions { AutoConnect = false });
            YeelightDevice? first = null;

            foreach (var text in texts)
            {
                if (!DeviceAnnouncement.TryParse(text, out var announcement) || !announcement!.IsFrom(IPAddress.Loopback))
                {
                    continue;
                }

                var device = client.Apply(announcement with { Id = "0xONLY" });

                Assert.NotNull(device);
                first ??= device;
                Assert.Same(first, device);
                Assert.Same(first, Assert.Single(client.Devices));
                Assert.Equal("0xONLY", device.Id);
            }
        }

        private static DeviceAnnouncement? Expected(byte[] bytes, IPAddress source)
        {
            if (bytes.Length is 0 or > MaxDatagramLength)
            {
                return null;
            }

            return DeviceAnnouncement.TryParse(Encoding.UTF8.GetString(bytes), out var announcement) && announcement!.IsFrom(source) ? announcement : null;
        }

        private static async Task HearSentinelAsync(ScriptedResponder sender, IPEndPoint listener, ConcurrentDictionary<string, TaskCompletionSource> sentinels, string id, Func<string> described)
        {
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            sentinels[id] = waiting;
            var message = Announcements.Message(id, "yeelight://127.0.0.1:55443", advertisement: true, ("model", "color"));
            var watch = Stopwatch.StartNew();
            do
            {
                await sender.SendAsync(listener, message);
                if (watch.Elapsed > Deadline)
                {
                    throw new XunitException($"The discovery did not announce the announcement that follows {described()}: it does not go on.");
                }
            }
            while (await Task.WhenAny(waiting.Task, Task.Delay(100)) != waiting.Task);

            sentinels.TryRemove(id, out _);
        }

        private static void SameAnnouncement(DeviceAnnouncement expected, DeviceAnnouncement actual, Func<string> described)
        {
            try
            {
                Assert.Equal(expected.Id, actual.Id);
                Assert.Equal(expected.Location, actual.Location);
                Assert.Equal(expected.Host, actual.Host);
                Assert.Equal(expected.Port, actual.Port);
                Assert.Equal(expected.Model, actual.Model);
                Assert.Equal(expected.FirmwareVersion, actual.FirmwareVersion);
                Assert.Equal(expected.IsAdvertisement, actual.IsAdvertisement);
                Assert.Equal(expected.Support.ToArray(), actual.Support.ToArray());
                Assert.Equal(expected.Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(), actual.Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray());
            }
            catch (Exception exception)
            {
                throw new XunitException($"The announcement is not what the readers make of {described()}\n{exception.GetType().Name}: {exception.Message}", exception);
            }
        }
    }
}
