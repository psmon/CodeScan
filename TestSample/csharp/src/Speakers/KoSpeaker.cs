namespace HelloWorld.Speakers;

public sealed class KoSpeaker : Person
{
    public KoSpeaker(string name) : base(name, "ko") { }

    public override string Speak() => "안녕, 세상!";
}
