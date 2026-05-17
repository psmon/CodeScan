namespace HelloAkka.Actors;

public sealed class JaSpeakerActor : PersonActor
{
    public JaSpeakerActor(string name) : base(name, "ja") { }

    protected override string Greeting() => "こんにちは、世界！";
}
