namespace Yeelight.Tests.TestApp
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;
    using Yeelight.TestApp.Models;
    using Xunit;

    /// <summary>A list of rows that is shown changes as little as it can, so that the screen does not flicker when one value of it changes.</summary>
    public sealed class PropertyRowsTests
    {
        [Fact]
        public void WhenOneValueChanges_OnlyThatRowIsReplaced()
        {
            var shown = new ObservableCollection<PropertyRow> { new("Power", "on"), new("Brightness", "100%"), new("Mode", "colour temperature") };
            var changes = new List<NotifyCollectionChangedEventArgs>();
            shown.CollectionChanged += (_, e) => changes.Add(e);

            PropertyRows.Sync(shown, [new("Power", "on"), new("Brightness", "50%"), new("Mode", "colour temperature")]);

            var change = Assert.Single(changes);
            Assert.Equal(NotifyCollectionChangedAction.Replace, change.Action);
            Assert.Equal(1, change.NewStartingIndex);
            Assert.Equal("50%", shown[1].Value);
        }

        [Fact]
        public void WhenNothingChanges_NothingIsRaised()
        {
            var shown = new ObservableCollection<PropertyRow> { new("Power", "on"), new("Brightness", "100%") };
            var changes = 0;
            shown.CollectionChanged += (_, _) => changes++;

            PropertyRows.Sync(shown, [new("Power", "on"), new("Brightness", "100%")]);

            Assert.Equal(0, changes);
        }

        [Fact]
        public void WhenTheNamesDiffer_TheListIsMadeAgain()
        {
            var shown = new ObservableCollection<PropertyRow> { new("Power", "on"), new("Brightness", "100%") };
            var fresh = new List<PropertyRow> { new("Power", "on"), new("Brightness", "100%"), new("Background Power", "off") };

            PropertyRows.Sync(shown, fresh);

            Assert.Equal(fresh, shown);
        }

        [Fact]
        public void WhenTheRowsAreTheSameButInAnotherOrder_TheListIsMadeAgain()
        {
            var shown = new ObservableCollection<PropertyRow> { new("A", "1"), new("B", "2") };

            PropertyRows.Sync(shown, [new("B", "2"), new("A", "1")]);

            Assert.Equal(["B", "A"], shown.Select(row => row.Name));
        }

        [Fact]
        public void WithoutRows_TheListIsEmpty()
        {
            var shown = new ObservableCollection<PropertyRow> { new("Power", "on") };

            PropertyRows.Sync(shown, []);

            Assert.Empty(shown);
        }

        [Fact]
        public void ANullListOrNullRows_IsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => PropertyRows.Sync(null!, []));
            Assert.Throws<ArgumentNullException>(() => PropertyRows.Sync([], null!));
        }
    }
}
