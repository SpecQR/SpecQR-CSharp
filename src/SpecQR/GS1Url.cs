using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SpecQR;

public static partial class GS1
{
    // Small URL adapter for the operations used by SpecQR. BCL handles IDNA and IPv6;
    // raw path/query escapes remain available for deterministic validation.
    private sealed class DigitalLinkUrl
    {
        internal string Scheme { get; }
        internal string Authority { get; }
        internal string Path { get; set; }
        internal string? Query { get; set; }
        internal string? Fragment { get; set; }
        public override string ToString() => Scheme + "://" + Authority + Path +
            (Query is null ? "" : "?" + Query) + (Fragment is null ? "" : "#" + Fragment);

        internal DigitalLinkUrl(string input)
        {
            if (input is null) throw InvalidUri();
            CheckTextLimit(input, "GS1 Digital Link URI");
            int left = 0, right = input.Length;
            while (left < right && input[left] <= 32) left++;
            while (right > left && input[right - 1] <= 32) right--;
            string trimmed = input[left..right].Replace("\t", "", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
            int colon = trimmed.IndexOf(':');
            if (colon <= 0) throw InvalidUri();
            string rawScheme = trimmed[..colon];
            if (!char.IsAsciiLetter(rawScheme[0]) || rawScheme.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.'))) throw InvalidUri();
            Scheme = rawScheme.ToLowerInvariant();
            string rest = trimmed[(colon + 1)..];
            int hash = rest.IndexOf('#');
            if (hash >= 0) { Fragment = rest[(hash + 1)..]; rest = rest[..hash]; }
            int question = rest.IndexOf('?');
            if (question >= 0) { Query = EncodeUrlPart(rest[(question + 1)..], true); rest = rest[..question]; }
            bool special = Scheme is "http" or "https" or "ftp" or "ws" or "wss";
            if (special) rest = rest.Replace('\\', '/').TrimStart('/');
            else if (rest.StartsWith("//", StringComparison.Ordinal)) rest = rest[2..];
            else { Authority = ""; Path = rest; return; }
            int slash = rest.IndexOf('/');
            if (slash < 0) slash = rest.Length;
            string authority = rest[..slash];
            if (authority.Length == 0) throw InvalidUri();
            string rawPath = slash == rest.Length ? "/" : rest[slash..];
            // Bound splitting before NormalizePath allocates a segment array. Edge slashes
            // are also counted here: the limit is a resource policy, not a GS1 constraint.
            if (rawPath.Count(c => c == '/') > MaxElements)
                throw Error($"GS1 Digital Link path must contain at most {MaxElements} segments");
            int at = authority.LastIndexOf('@');
            string credentials = at < 0 ? "" : EncodeCredentials(authority[..at]) + "@";
            string hostPort = at < 0 ? authority : authority[(at + 1)..];
            string rawHost, rawPort = "";
            if (hostPort.StartsWith('['))
            {
                int bracket = hostPort.LastIndexOf(']');
                if (bracket < 0) throw InvalidUri();
                rawHost = hostPort[..(bracket + 1)];
                string suffix = hostPort[(bracket + 1)..];
                if (suffix.Length > 0)
                {
                    if (suffix[0] != ':') throw InvalidUri();
                    rawPort = suffix[1..];
                }
            }
            else
            {
                int portColon = hostPort.LastIndexOf(':');
                rawHost = portColon < 0 ? hostPort : hostPort[..portColon];
                if (portColon >= 0) rawPort = hostPort[(portColon + 1)..];
            }
            string port = "";
            if (rawPort.Length > 0)
            {
                if (!IsDigits(rawPort) || !uint.TryParse(rawPort, NumberStyles.None, CultureInfo.InvariantCulture, out uint parsedPort) || parsedPort > 65535) throw InvalidUri();
                if (!((Scheme is "https" or "wss" && parsedPort == 443) || (Scheme is "http" or "ws" && parsedPort == 80) || (Scheme == "ftp" && parsedPort == 21)))
                    port = ":" + parsedPort.ToString(CultureInfo.InvariantCulture);
            }
            Authority = credentials + NormalizeHost(rawHost) + port;
            Path = NormalizePath(EncodeUrlPart(rawPath, false));
        }

        private static SpecQRException InvalidUri() => Error("GS1 Digital Link URI must be an absolute http or https URL");
        private static string NormalizeHost(string rawHost)
        {
            if (rawHost.Length == 0) throw InvalidUri();
            if (rawHost.StartsWith('['))
            {
                string address = rawHost[1..^1];
                if (address.Contains('%') || !IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetworkV6) throw InvalidUri();
                // Render all IPv6 groups numerically, including embedded IPv4, like WHATWG.
                byte[] bytes = ip.GetAddressBytes();
                ushort[] numbers = Enumerable.Range(0, 8).Select(i => (ushort)(bytes[i * 2] * 256 + bytes[i * 2 + 1])).ToArray();
                int bestStart = 0, bestLength = 0;
                for (int i = 0; i < 8;)
                {
                    if (numbers[i] != 0) { i++; continue; }
                    int end = i;
                    while (end < 8 && numbers[end] == 0) end++;
                    if (end - i > bestLength) { bestStart = i; bestLength = end - i; }
                    i = end;
                }
                string[] groups = numbers.Select(n => n.ToString("x", CultureInfo.InvariantCulture)).ToArray();
                return bestLength > 1
                    ? "[" + string.Join(":", groups.Take(bestStart)) + "::" + string.Join(":", groups.Skip(bestStart + bestLength)) + "]"
                    : "[" + string.Join(":", groups) + "]";
            }
            string host = PercentDecode(rawHost, false, true) ?? throw InvalidUri();
            if (host.Length == 0 || host.Any(c => c <= 32 || c == 127 || "#/:<>?@[\\]^|%".Contains(c))) throw InvalidUri();
            try { host = new IdnMapping().GetAscii(host).ToLowerInvariant(); }
            catch (ArgumentException) { throw InvalidUri(); }
            string[] pieces = host.TrimEnd('.').Split('.');
            string last = pieces[^1];
            bool hexadecimalLast = last.StartsWith("0x", StringComparison.Ordinal) && last.AsSpan(2).ToArray().All(c => Hex(c) >= 0);
            if (!(IsDigits(last) || hexadecimalLast)) return host;
            if (pieces.Length > 4) throw InvalidUri();
            var numbers4 = new ulong[pieces.Length];
            for (int i = 0; i < pieces.Length; i++)
            {
                string piece = pieces[i];
                int radix = 10, start = 0;
                if (piece.StartsWith("0x", StringComparison.Ordinal)) { radix = 16; start = 2; }
                else if (piece.Length >= 2 && piece[0] == '0') { radix = 8; start = 1; }
                if (piece.Length == 0) throw InvalidUri();
                ulong value = 0;
                for (int j = start; j < piece.Length; j++)
                {
                    int digit = Hex(piece[j]);
                    if (digit < 0 || digit >= radix || value > (ulong.MaxValue - (uint)digit) / (uint)radix) throw InvalidUri();
                    value = value * (uint)radix + (uint)digit;
                }
                numbers4[i] = value;
            }
            if (numbers4.Take(numbers4.Length - 1).Any(n => n > 255) || numbers4[^1] >= (1UL << (8 * (5 - numbers4.Length)))) throw InvalidUri();
            ulong ipv4 = numbers4[^1];
            for (int i = 0; i < numbers4.Length - 1; i++) ipv4 += numbers4[i] << (8 * (3 - i));
            return string.Join(".", new[] { 24, 16, 8, 0 }.Select(shift => ((ipv4 >> shift) & 255).ToString(CultureInfo.InvariantCulture)));
        }

        internal static string NormalizePath(string path)
        {
            var result = new List<string>();
            string[] pieces = path.Split('/');
            for (int i = 0; i < pieces.Length; i++)
            {
                string dot = pieces[i].Replace("%2e", ".", StringComparison.OrdinalIgnoreCase);
                if (dot is "." or "..")
                {
                    if (dot == ".." && result.Count > 1) result.RemoveAt(result.Count - 1);
                    if (i == pieces.Length - 1) result.Add("");
                }
                else result.Add(pieces[i]);
            }
            string joined = string.Join("/", result);
            return joined.StartsWith('/') ? joined : "/" + joined;
        }
        private static string EncodeUrlPart(string text, bool query)
        {
            string forbidden = query ? "\"#'<>" : "\"#<>?`{}";
            var builder = new StringBuilder(text.Length);
            foreach (byte b in Encoding.UTF8.GetBytes(text))
            {
                if (b <= 32 || b > 126 || forbidden.Contains((char)b)) builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                else builder.Append((char)b);
            }
            return builder.ToString();
        }
        private static string EncodeCredentials(string text)
        {
            var builder = new StringBuilder();
            // The first colon divides user/password; subsequent colons are escaped.
            bool colonSeen = false;
            foreach (byte b in Encoding.UTF8.GetBytes(text))
            {
                if (b == ':' && !colonSeen) { builder.Append(':'); colonSeen = true; }
                else if (b <= 32 || b > 126 || "\"#<>?`{}/:;=@[\\]^|".Contains((char)b)) builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                else builder.Append((char)b);
            }
            return builder.ToString();
        }
    }
}
