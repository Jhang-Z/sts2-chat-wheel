using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace VoiceRoulette.TTS;

public sealed class AudioCache
{
    private const string FormatVersion = "v1";

    private readonly string _dir;
    private readonly long _maxBytes;
    private readonly object _gate = new();

    public AudioCache(string dir, long maxBytes)
    {
        _dir = dir;
        _maxBytes = maxBytes;
        Directory.CreateDirectory(_dir);
    }

    public static string Key(string text, string voice)
    {
        var input = $"{text}|{voice}|{FormatVersion}";
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public bool TryGet(string key, out string? path)
    {
        var p = PathFor(key);
        if (File.Exists(p))
        {
            File.SetLastAccessTimeUtc(p, DateTime.UtcNow);
            path = p;
            return true;
        }
        path = null;
        return false;
    }

    public void Put(string key, byte[] bytes)
    {
        lock (_gate)
        {
            File.WriteAllBytes(PathFor(key), bytes);
            EvictIfOverCapacity();
        }
    }

    private string PathFor(string key) => Path.Combine(_dir, $"{key}.mp3");

    private void EvictIfOverCapacity()
    {
        var files = new DirectoryInfo(_dir).GetFiles("*.mp3")
            .OrderBy(f => f.LastWriteTimeUtc)
            .ToList();
        long total = files.Sum(f => f.Length);
        var i = 0;
        while (total > _maxBytes && i < files.Count)
        {
            total -= files[i].Length;
            files[i].Delete();
            i++;
        }
    }
}
