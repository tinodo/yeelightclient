namespace Yeelight.Tests.Fuzz
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Text;
    using FsCheck;
    using FsCheck.Fluent;
    using Xunit.Abstractions;
    using Yeelight.Tests.TestSupport;

    /// <summary>
    /// What the readers of the messages that anyone on the network can send promise for any input: they never throw, they accept only what
    /// is the announcement of a light, they keep what they take within bounds, and an address is only trusted when the message came from it.
    /// </summary>
    [Trait("Category", "Fuzz")]
    public class AnnouncementPropertyTests(ITestOutputHelper output)
    {
        [Fact]
        public void AnAnnouncement_IsReadWithoutThrowing_AndWhatIsAcceptedIsWithinBounds()
        {
            FuzzCheck.ForAll(
                FuzzGenerators.Announcement(),
                text =>
                {
                    if (DeviceAnnouncement.TryParse(text, out var announcement))
                    {
                        Assert.NotNull(announcement);
                        Assert.InRange(text.Length, 1, 4096);
                        Assert.InRange(announcement.Id.Length, 1, 64);
                        Assert.InRange(announcement.Port, 1, 65535);
                        Assert.NotEmpty(announcement.Host);
                        Assert.StartsWith("yeelight://", announcement.Location, StringComparison.OrdinalIgnoreCase);
                        Assert.NotNull(announcement.Model);
                        Assert.NotNull(announcement.FirmwareVersion);
                        Assert.All(announcement.Support, method =>
                        {
                            Assert.NotEmpty(method);
                            Assert.False(method.Any(char.IsWhiteSpace), "A method of the list has white space in it.");
                        });
                        Assert.All(announcement.Properties, property =>
                        {
                            Assert.True(YeelightProperties.IsKnown(property.Key), property.Key + " is not a property.");
                            Assert.InRange(property.Value.Length, 0, 256);
                        });
                    }

                    return true;
                },
                seedOffset: 20);

            output.WriteLine(FuzzSettings.Description);
        }

        [Fact]
        public void TheReaderOfAnnouncements_NeverThrows_ForAnyText()
        {
            Assert.False(DeviceAnnouncement.TryParse(null, out _));
            FuzzCheck.ForAll(
                FuzzGenerators.Text,
                text =>
                {
                    _ = DeviceAnnouncement.TryParse(text, out _);
                    return true;
                },
                seedOffset: 21);
        }

        [Fact]
        public void AnAnnouncementThatIsWritten_IsReadAsItWasWritten()
        {
            var names = new[] { "power", "bright", "ct", "rgb", "hue", "sat", "color_mode", "name" };
            var announcement =
                from advertisement in Gen.Elements(true, false)
                from id in Gen.Choose(0, int.MaxValue).Select(number => "0x" + number.ToString("x16", CultureInfo.InvariantCulture))
                from port in Gen.Choose(1, 65535)
                from model in FuzzGenerators.CleanText.Select(text => text.Trim())
                from values in Gen.ListOf(FuzzGenerators.CleanText.Select(text => text.Trim()))
                select (Advertisement: advertisement, Id: id, Port: port, Model: model, Values: values.Take(names.Length).ToArray());

            FuzzCheck.ForAll(
                announcement,
                sample =>
                {
                    var headers = new List<(string Name, string Value)> { ("model", sample.Model), ("support", "get_prop set_power") };
                    for (var index = 0; index < sample.Values.Length; index++)
                    {
                        headers.Add((names[index], sample.Values[index]));
                    }

                    var text = Announcements.Message(sample.Id, "yeelight://127.0.0.1:" + sample.Port.ToString(CultureInfo.InvariantCulture), sample.Advertisement, [.. headers]);
                    if (text.Length > 4096)
                    {
                        return true;
                    }

                    Assert.True(DeviceAnnouncement.TryParse(text, out var read), text);
                    Assert.Equal(sample.Id, read!.Id);
                    Assert.Equal(sample.Port, read.Port);
                    Assert.Equal("127.0.0.1", read.Host);
                    Assert.Equal(sample.Advertisement, read.IsAdvertisement);
                    Assert.Equal(sample.Model, read.Model);
                    string[] methods = ["get_prop", "set_power"];
                    Assert.Equal(methods, read.Support);
                    for (var index = 0; index < sample.Values.Length; index++)
                    {
                        if (sample.Values[index].Length <= 256)
                        {
                            Assert.Equal(sample.Values[index], read.Properties[names[index]]);
                        }
                    }

                    return true;
                },
                seedOffset: 22);
        }

        [Fact]
        public void AnAddress_IsTrustedOnlyWhenTheMessageCameFromIt_AndItIsOnALocalNetwork()
        {
            var cases =
                from text in FuzzGenerators.Announcement()
                from source in FuzzGenerators.Address()
                from other in FuzzGenerators.Address()
                select (Text: text, Source: source, Other: other);

            FuzzCheck.ForAll(
                cases,
                sample =>
                {
                    if (!DeviceAnnouncement.TryParse(sample.Text, out var announcement))
                    {
                        return true;
                    }

                    var fromSource = announcement!.IsFrom(sample.Source);
                    var fromOther = announcement.IsFrom(sample.Other);

                    // The address is the source of the message, and a source that a local network has: never a host that the sender names for another.
                    if (fromSource)
                    {
                        Assert.True(DeviceAnnouncement.IsLocalNetworkAddress(sample.Source));
                        Assert.True(IPAddress.TryParse(announcement.Host, out var named) && named.Equals(sample.Source));
                    }

                    // One message names one address: it cannot be from two.
                    Assert.False(fromSource && fromOther && !sample.Source.Equals(sample.Other), "A message was taken as from two different addresses.");

                    // The same address, written as IPv6 does for an IPv4 address.
                    Assert.Equal(fromSource, announcement.IsFrom(sample.Source.MapToIPv6()));
                    return true;
                },
                seedOffset: 23);
        }

        [Fact]
        public void ALocation_IsReadWithoutThrowing_ForAnyText()
        {
            Assert.False(YeelightLocation.TryParse(null, out _, out _));
            var texts = Gen.Frequency(
                (3, FuzzGenerators.Text),
                (3, FuzzGenerators.Text.Select(text => "yeelight://" + text)),
                (3, from host in FuzzGenerators.Text from port in FuzzGenerators.Text select "yeelight://" + host + ":" + port),
                (2, Gen.Elements("yeelight://", "yeelight://:", "yeelight:///", "yeelight://a:1/", "YEELIGHT://a:1", "yeelight://a:-1", "yeelight://a:+1", "yeelight://a:1 ", " yeelight://a:1")));

            FuzzCheck.ForAll(
                texts,
                text =>
                {
                    if (YeelightLocation.TryParse(text, out var host, out var port))
                    {
                        Assert.NotEmpty(host);
                        Assert.InRange(port, 1, 65535);
                    }

                    return true;
                },
                seedOffset: 24);
        }

        [Fact]
        public void ALocation_ThatIsWritten_IsReadAsItWasWritten()
        {
            var locations =
                from host in Gen.Frequency((3, Gen.Elements("127.0.0.1", "192.168.1.239", "::1", "fe80::1")), (3, FuzzGenerators.CleanText.Where(text => text.Length > 0)))
                from port in Gen.Choose(1, 65535)
                select (Host: host, Port: port);

            FuzzCheck.ForAll(
                locations,
                sample =>
                {
                    var text = YeelightLocation.Format(sample.Host, sample.Port);
                    Assert.True(YeelightLocation.TryParse(text, out var host, out var port), text);
                    Assert.Equal(sample.Host, host);
                    Assert.Equal(sample.Port, port);
                    return true;
                },
                seedOffset: 25);
        }
    }
}
