namespace HelloWorld.Speakers;

public sealed class JaSpeaker : Person
{
    public JaSpeaker(string name) : base(name, "ja") { }

    public override string Speak() => "こんにちは、世界！";
}
