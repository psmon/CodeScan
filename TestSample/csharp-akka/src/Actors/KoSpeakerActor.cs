namespace HelloAkka.Actors;

public sealed class KoSpeakerActor : PersonActor
{
    public KoSpeakerActor(string name) : base(name, "ko") { }

    protected override string Greeting() => "안녕, 세상!";
}
