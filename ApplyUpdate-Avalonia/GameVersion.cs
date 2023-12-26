using System;
using System.Collections.Generic;

namespace ApplyUpdate;

public struct GameVersion
{
    public GameVersion(int major, int minor, int build, int revision = 0)
    {
        Major = major;
        Minor = minor;
        Build = build;
        Revision = revision;
    }

    public GameVersion(ReadOnlySpan<int> ver)
    {
        if (!(ver.Length == 3 || ver.Length == 4))
        {
            throw new ArgumentException($"Version array entered should have length of 3 or 4!");
        }

        Major = ver[0];
        Minor = ver[1];
        Build = ver[2];
        Revision = 0;
        if (ver.Length == 4)
        {
            Revision = ver[3];
        }
    }

    public GameVersion(Version version)
    {
        Major = version.Major;
        Minor = version.Minor;
        Build = version.Build;
        Revision = 0;
    }

    public GameVersion(string version)
    {
        string[] ver = version.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (!(ver.Length == 3 || ver.Length == 4))
        {
            throw new ArgumentException($"Version in the config.ini should be in \"x.x.x\" or \"x.x.x.x\" format! (current value: \"{version}\")");
        }

        Revision = 0;
        if (!int.TryParse(ver[0], out Major)) throw new ArgumentException($"Major version is not a number! (current value: {ver[0]}");
        if (!int.TryParse(ver[1], out Minor)) throw new ArgumentException($"Minor version is not a number! (current value: {ver[1]}");
        if (!int.TryParse(ver[2], out Build)) throw new ArgumentException($"Build version is not a number! (current value: {ver[2]}");
        if (ver.Length == 4)
        {
            if (!int.TryParse(ver[3], out Revision)) throw new ArgumentException($"Revision version is not a number! (current value: {ver[3]}");
        }
    }

    public bool IsMatch(string versionToCompare)
    {
        GameVersion parsed = new GameVersion(versionToCompare);
        return IsMatch(parsed);
    }

    public bool IsMatch(GameVersion versionToCompare) => Major == versionToCompare.Major && Minor == versionToCompare.Minor && Build == versionToCompare.Build && Revision == versionToCompare.Revision;

    public GameVersion GetIncrementedVersion()
    {
        int NextMajor = Major;
        int NextMinor = Minor;

        NextMinor++;
        if (NextMinor >= 10)
        {
            NextMinor = 0;
            NextMajor++;
        }

        return new GameVersion(new int[] { NextMajor, NextMinor, Build, Revision });
    }

    public Version ToVersion() => new Version(Major, Minor, Build, Revision);
    public override string ToString() => $"{Major}.{Minor}.{Build}";

    public string VersionStringManifest { get => string.Join(".", VersionArrayManifest); }
    public string VersionString { get => string.Join(".", VersionArray); }
    public int[] VersionArrayManifest { get => new int[4] { Major, Minor, Build, Revision }; }
    public int[] VersionArray { get => new int[3] { Major, Minor, Build }; }
    public readonly int Major;
    public readonly int Minor;
    public readonly int Build;
    public readonly int Revision;
}

public class AppUpdateVersionProp
{
    public string ver { get; set; }
    public long time { get; set; }
    public List<AppUpdateVersionFileProp> f { get; set; }
}

public class AppUpdateVersionFileProp
{
    public string p { get; set; }
    public string crc { get; set; }
    public long s { get; set; }
}
