using Akka.Actor;
using HelloAkka.Messages;

namespace HelloAkka.Actors;

/// <summary>
/// 부모 액터. PreStart에서 En/Ko/Ja Speaker를 자식으로 생성하고
/// HelloAll 메시지를 받으면 모든 자식에게 SayHello를 발송한다.
/// </summary>
public sealed class WorldActor : ReceiveActor
{
    private IActorRef _en = ActorRefs.Nobody;
    private IActorRef _ko = ActorRefs.Nobody;
    private IActorRef _ja = ActorRefs.Nobody;

    public WorldActor()
    {
        Receive<HelloAll>(_ =>
        {
            _en.Tell(new SayHello());
            _ko.Tell(new SayHello());
            _ja.Tell(new SayHello());
        });

        Receive<HelloResponse>(r =>
        {
            Console.WriteLine($"[{r.Language}] {r.Name}: {r.Greeting}");
        });
    }

    protected override void PreStart()
    {
        _en = Context.ActorOf(Props.Create(() => new EnSpeakerActor("Alice")), "en");
        _ko = Context.ActorOf(Props.Create(() => new KoSpeakerActor("진수")), "ko");
        _ja = Context.ActorOf(Props.Create(() => new JaSpeakerActor("ハナコ")), "ja");
        base.PreStart();
    }
}
