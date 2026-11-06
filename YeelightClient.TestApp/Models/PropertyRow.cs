namespace Yeelight.TestApp.Models
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;

    /// <summary>One line of the properties of a light: a name and what it is.</summary>
    /// <param name="Name">The name of the property, for a person.</param>
    /// <param name="Value">The value, as text.</param>
    internal sealed record PropertyRow(string Name, string Value);

    /// <summary>What a list of <see cref="PropertyRow"/> needs.</summary>
    internal static class PropertyRows
    {
        /// <summary>
        /// Makes a list show new rows with as little change as possible: a list that is shown does not flicker when one value of it changes,
        /// as it does when every row is removed and added again.
        /// </summary>
        /// <param name="target">The list that is shown.</param>
        /// <param name="fresh">The rows that it has to hold.</param>
        public static void Sync(ObservableCollection<PropertyRow> target, IReadOnlyList<PropertyRow> fresh)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(fresh);
            if (target.Count == fresh.Count && target.Select(row => row.Name).SequenceEqual(fresh.Select(row => row.Name), StringComparer.Ordinal))
            {
                for (var index = 0; index < fresh.Count; index++)
                {
                    if (target[index] != fresh[index])
                    {
                        target[index] = fresh[index];
                    }
                }

                return;
            }

            target.Clear();
            foreach (var row in fresh)
            {
                target.Add(row);
            }
        }
    }
}
