using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Daemon.Divination;

/// <summary>
/// The draw generator: xoshiro128** seeded from SHA-256 of a seed string. It uses only 32-bit operations, so the
/// same algorithm in TypeScript (Math.imul, >>> 0) gives the same draws on the phone and on the server, and any
/// seeded draw can be re-run to check it. A fresh seed comes from the operating system's secure random source.
/// </summary>
public sealed class DrawRng
{
    private uint _s0, _s1, _s2, _s3;

    public DrawRng(string seed)
    {
        Seed = seed;
        var h = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        _s0 = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(0));
        _s1 = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(4));
        _s2 = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(8));
        _s3 = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(12));
        if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1;
    }

    /// <summary>The seed this generator was built from; a receipt keeps it so the draw can be checked.</summary>
    public string Seed { get; }

    /// <summary>A generator with 128 bits of fresh secure randomness as its seed.</summary>
    public static DrawRng Fresh() => new(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)));

    private static uint Rotl(uint x, int k) => (x << k) | (x >> (32 - k));

    public uint NextUInt()
    {
        uint result = Rotl(_s1 * 5, 7) * 9;
        uint t = _s1 << 9;
        _s2 ^= _s0; _s3 ^= _s1; _s1 ^= _s2; _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 11);
        return result;
    }

    /// <summary>A uniform integer in [0, n), without modulo bias (rejection sampling).</summary>
    public int Below(int n)
    {
        if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
        uint threshold = (uint)((1UL << 32) % (uint)n);
        while (true)
        {
            uint r = NextUInt();
            if (r >= threshold) return (int)(r % (uint)n);
        }
    }

    public double NextDouble() => NextUInt() / 4294967296.0;

    public bool Chance(double p) => NextDouble() < p;

    /// <summary>k distinct items in draw order (partial Fisher–Yates shuffle).</summary>
    public List<T> Choose<T>(IReadOnlyList<T> items, int k)
    {
        if (k > items.Count) throw new ArgumentOutOfRangeException(nameof(k));
        var idx = Enumerable.Range(0, items.Count).ToArray();
        for (int i = 0; i < k; i++)
        {
            int j = i + Below(idx.Length - i);
            (idx[i], idx[j]) = (idx[j], idx[i]);
        }
        return idx.Take(k).Select(i => items[i]).ToList();
    }
}

/// <summary>Seeds for draws.</summary>
public static class Seeds
{
    /// <summary>
    /// The seed of a user's daily draw: the same all day in the user's own time zone, different per method, and
    /// unguessable without the server's secret (HMAC), so nobody can compute other people's cards in advance.
    /// </summary>
    public static string Daily(byte[] secret, string userId, DateOnly localDate, string method) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(
            $"daily|{userId}|{localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}|{method}")));

    /// <summary>The calendar date at this instant in the given IANA time zone (e.g. "America/Mexico_City").</summary>
    public static DateOnly LocalDate(DateTimeOffset instant, string timeZoneId) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).DateTime);
}

/// <summary>The embedded data files (Data/*.json), loaded once.</summary>
internal static class Data
{
    private static readonly Dictionary<string, JsonNode> Cache = new();

    public static JsonNode Get(string name)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var node)) return node;
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name + ".json")
                ?? throw new InvalidOperationException($"missing data file {name}.json");
            node = JsonNode.Parse(s)!;
            Cache[name] = node;
            return node;
        }
    }

    public static string S(JsonNode? n) => n?.GetValue<string>() ?? "";
}

internal static class Canonical
{
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Json(JsonNode node) => node.ToJsonString(Compact);

    public static string Hash(JsonNode node) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Json(node))));
}
