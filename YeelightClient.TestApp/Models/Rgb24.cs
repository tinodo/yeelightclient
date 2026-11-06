namespace Yeelight.TestApp.Models
{
    using System.Globalization;

    /// <summary>A colour of 8 bits a channel, which is how a light and a swatch on the screen say it.</summary>
    /// <param name="R">The red part, 0 to 255.</param>
    /// <param name="G">The green part, 0 to 255.</param>
    /// <param name="B">The blue part, 0 to 255.</param>
    internal readonly record struct Rgb24(byte R, byte G, byte B)
    {
        /// <summary>Gets the colour as a number, the way the protocol says it: red times 65536, plus green times 256, plus blue.</summary>
        public int Value => (this.R << 16) | (this.G << 8) | this.B;

        /// <summary>Gets the colour as text for a person: <c>#FF8C00</c>.</summary>
        public string Hex => string.Create(CultureInfo.InvariantCulture, $"#{this.R:X2}{this.G:X2}{this.B:X2}");

        /// <summary>Makes a colour of the number of the protocol.</summary>
        /// <param name="value">Red times 65536, plus green times 256, plus blue; the bits above 24 are ignored.</param>
        /// <returns>The colour.</returns>
        public static Rgb24 FromValue(int value) => new((byte)((value >> 16) & 0xFF), (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF));
    }
}
