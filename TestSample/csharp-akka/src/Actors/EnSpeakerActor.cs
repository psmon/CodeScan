namespace HelloAkka.Actors;

public sealed class EnSpeakerActor : PersonActor
{
    public EnSpeakerActor(string name) : base(name, "en") { }

    protected override string Greeting() => "Hello, World!";
}
