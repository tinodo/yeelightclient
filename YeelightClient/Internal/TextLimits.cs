namespace Yeelight
{
    using System;

    /// <summary>
    /// Limits on the text that a device sends.
    /// </summary>
    internal static class TextLimits
    {
        /// <summary>
        /// Cuts a text to at most <paramref name="maxLength"/> characters, never in the middle of a surrogate pair (an emoji in a name): half of
        /// one is not text, and breaks whatever it is written to.
        /// </summary>
        /// <param name="value">The text.</param>
        /// <param name="maxLength">The most characters, at least one.</param>
        /// <returns>The text, or its start.</returns>
        public static string Truncate(string value, int maxLength)
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

            if (value.Length <= maxLength)
            {
                return value;
            }

            var length = char.IsHighSurrogate(value[maxLength - 1]) && char.IsLowSurrogate(value[maxLength]) ? maxLength - 1 : maxLength;
            return value[..length];
        }
    }
}
