namespace Yeelight.TestApp.Services
{
    using System;
    using System.Globalization;
    using System.Text;

    /// <summary>Puts the answer of a light to a raw command into words, for the Raw page and its history.</summary>
    internal static class RawFormat
    {
        /// <summary>Writes everything that the library says about an answer, one thing on each line.</summary>
        /// <param name="result">The answer.</param>
        /// <param name="elapsed">How long the light took to answer.</param>
        /// <returns>The text: the id, whether it worked, the error, the values (as JSON) and the time.</returns>
        public static string Details(YeelightResult result, TimeSpan elapsed)
        {
            ArgumentNullException.ThrowIfNull(result);
            var text = new StringBuilder();
            text.Append("Id:       ").Append(result.Id.ToString(CultureInfo.InvariantCulture)).AppendLine();
            text.Append("Success:  ").Append(result.Success ? "true" : "false").AppendLine();
            text.Append("Error:    ").AppendLine(result.Error is { } error ? ErrorText(error) : "none");
            var values = JsonTools.ValuesToJson(result.Values);
            text.Append("Values:   ").AppendLine(values.Length <= 70 ? values : Environment.NewLine + JsonTools.ValuesToIndentedJson(result.Values));
            text.Append("Elapsed:  ").Append(Outcome.FormatElapsed(elapsed));
            return text.ToString();
        }

        /// <summary>Writes the error of an answer on one line.</summary>
        /// <param name="error">The error.</param>
        /// <returns>For example <c>code -1, "unsupported method", kind DeviceRejected</c>.</returns>
        public static string ErrorText(YeelightError error)
        {
            ArgumentNullException.ThrowIfNull(error);
            return string.Create(CultureInfo.InvariantCulture, $"code {error.Code}, \"{error.Message}\", kind {error.Kind}");
        }

        /// <summary>Writes how a command went, on one line, for the history.</summary>
        /// <param name="result">The answer.</param>
        /// <returns>For example <c>ok</c>, or <c>error -1: unsupported method</c>.</returns>
        public static string Summary(YeelightResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            return result.Error is { } error
                ? string.Create(CultureInfo.InvariantCulture, $"error {error.Code}: {error.Message}")
                : result.Values.Count == 0 ? "success, no values" : JsonTools.OneLine(JsonTools.ValuesToJson(result.Values), 80);
        }

        /// <summary>Makes the outcome that the page shows in its bar for an answer.</summary>
        /// <param name="method">The method that was sent.</param>
        /// <param name="result">The answer.</param>
        /// <param name="elapsed">How long the light took to answer.</param>
        /// <returns>A success when the light accepted the command, a warning when it refused it, and an error when the command did not get through.</returns>
        public static Outcome ToOutcome(string method, YeelightResult result, TimeSpan elapsed)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (result.Error is not { } error)
            {
                return Outcome.Succeeded(method, "Values " + JsonTools.OneLine(JsonTools.ValuesToJson(result.Values), 80), elapsed);
            }

            var details = string.Create(CultureInfo.InvariantCulture, $"Kind {error.Kind}, error code {error.Code}: {JsonTools.OneLine(error.Message)} (after {Outcome.FormatElapsed(elapsed)}).");
            return error.IsTransportFailure
                ? new Outcome(OutcomeSeverity.Error, $"{method}: it did not get through", details, elapsed)
                : new Outcome(OutcomeSeverity.Warning, $"{method}: the light refused it", details, elapsed);
        }
    }
}
