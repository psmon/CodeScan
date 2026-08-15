using CodeScan.Services.Harvest;
using Xunit;

namespace CodeScan.Tests;

/// <summary>
/// Verifies the build-artifact harvest path for the JVM: resolved inheritance is
/// read straight out of compiled <c>.class</c> bytecode, no JVM toolchain needed.
/// The fixture is a real EnSpeaker.class (compiled from TestSample/java, class
/// EnSpeaker extends helloworld.Person) embedded as base64 so the test is portable.
/// </summary>
public class JvmClassHarvesterTests
{
    // TestSample/java: `public final class EnSpeaker extends Person` (helloworld.speakers).
    private const string EnSpeakerClassB64 =
        "yv66vgAAAEUAFAgAAgEAAmVuCgAEAAUHAAYMAAcACAEAEWhlbGxvd29ybGQvUGVyc29uAQAGPGluaXQ+" +
        "AQAnKExqYXZhL2xhbmcvU3RyaW5nO0xqYXZhL2xhbmcvU3RyaW5nOylWCAAKAQANSGVsbG8sIFdvcmxkIQcA" +
        "DAEAHWhlbGxvd29ybGQvc3BlYWtlcnMvRW5TcGVha2VyAQAVKExqYXZhL2xhbmcvU3RyaW5nOylWAQAEQ29k" +
        "ZQEAD0xpbmVOdW1iZXJUYWJsZQEABXNwZWFrAQAUKClMamF2YS9sYW5nL1N0cmluZzsBAApTb3VyY2VGaWxl" +
        "AQAORW5TcGVha2VyLmphdmEAMQALAAQAAAAAAAIAAQAHAA0AAQAOAAAAJAADAAIAAAAIKisSAbcAA7EAAAAB" +
        "AA8AAAAKAAIAAAAHAAcACAABABAAEQABAA4AAAAbAAEAAQAAAAMSCbAAAAABAA8AAAAGAAEAAAAMAAEAEgAA" +
        "AAIAEw==";

    [Fact]
    public void Read_ExtractsResolvedBaseClass_FromBytecode()
    {
        var bytes = System.Convert.FromBase64String(EnSpeakerClassB64);

        var hc = JvmClassHarvester.Read(bytes);

        Assert.NotNull(hc);
        Assert.Equal("helloworld/speakers/EnSpeaker", hc!.Name);
        Assert.Equal("helloworld/Person", hc.SuperName);   // cross-file base, resolved by the compiler
        Assert.Empty(hc.Interfaces);
    }

    [Fact]
    public void ToDependencies_YieldsInheritsEdge_ToSimpleName()
    {
        var hc = JvmClassHarvester.Read(System.Convert.FromBase64String(EnSpeakerClassB64))!;

        var deps = JvmClassHarvester.ToDependencies(hc);

        var edge = Assert.Single(deps);
        Assert.Equal("EnSpeaker", edge.FromName);
        Assert.Equal("inherits_or_implements", edge.EdgeKind);
        Assert.Equal("Person", edge.ToName);
        Assert.Equal("helloworld.Person", edge.Detail);
        Assert.Equal("jvm-class", edge.Strategy);
    }

    [Fact]
    public void Read_ReturnsNull_ForNonClassBytes()
    {
        Assert.Null(JvmClassHarvester.Read(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 }));
        Assert.Null(JvmClassHarvester.Read([]));
    }
}
