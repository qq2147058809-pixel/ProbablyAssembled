using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PCExpansion;

/// <summary>hm-econ-v1. Each observation is independent of call order and batch size.</summary>
internal static class ServerRoomEconomyRandom
{
    internal const string Version = "hm-econ-v1";

    internal static double Uniform(string run, int slot, string domain, int coin, int day)
    {
        if (string.IsNullOrWhiteSpace(run) || slot < 0 || day < 0 || coin < 0 || coin >= 5)
            throw new ArgumentException("经济随机上下文无效。");
        switch (domain)
        {
            case "pool": case "industry-shared": case "industry-coin": case "industry-duration":
            case "electricity-start": case "electricity-rate": case "electricity-duration": break;
            default: throw new ArgumentException("经济随机域无效。");
        }
        if ((domain == "industry-shared" || domain.StartsWith("electricity-", StringComparison.Ordinal)) && coin != 0)
            throw new ArgumentException("共同随机域必须使用币索引0。");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Version + "|" + run + "|" +
            slot.ToString(CultureInfo.InvariantCulture) + "|" + domain + "|" + coin.ToString(CultureInfo.InvariantCulture)));
        ulong key = 0;
        for (var i = 0; i < 8; i++) key |= (ulong)bytes[i] << (8 * i);
        unchecked
        {
            var z = key + ((ulong)day + 1UL) * 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (z >> 11) / 9007199254740992.0;
        }
    }

    internal static decimal PoolLuck(string run, int slot, int coin, int day)
    {
        var u = Uniform(run, slot, "pool", coin, day);
        return u < .1 ? .70m : u < .3 ? .85m : u < .7 ? 1m : u < .9 ? 1.15m : 1.30m;
    }
}
