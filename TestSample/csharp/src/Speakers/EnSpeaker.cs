namespace HelloWorld.Speakers;

public sealed class EnSpeaker : Person
{
    public EnSpeaker(string name) : base(name, "en") { }

    public override string Speak() => "Hello, World!";
}
