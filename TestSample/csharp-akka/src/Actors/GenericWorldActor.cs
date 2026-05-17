using Akka.Actor;
using HelloAkka.Messages;

namespace HelloAkka.Actors;

/// <summary>
/// WorldActor와 동일한 동작이지만 자식 생성에 **type-parameter 형태의 Props.Create&lt;T&gt;()** 와
/// **Props.Create(typeof(T), args)** 를 사용한다. 람다 안에 <c>new</c> 표현식이 없어서
/// 현재 정규식 전략은 <c>WorldActor -[creates]-&gt; EnSpeakerActor</c> 엣지를 만들지 못한다.
/// regex 전략 vs 의미 분석 전략의 차이를 측정하기 위한 fixture.
/// </summary>
public sealed class GenericWorldActor : ReceiveActor
{
    private IActorRef _en = ActorRefs.Nobody;
    private IActorRef _ko = ActorRefs.Nobody;
    private IActorRef _ja = ActorRefs.Nobody;

    public GenericWorldActor()
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
        // typeof-style: regex can see EnSpeakerActor as a token but cannot tie
        // it to a parent-child actor edge.
        _en = Context.ActorOf(Props.Create(typeof(EnSpeakerActor), "Alice"), "en");
        _ko = Context.ActorOf(Props.Create(typeof(KoSpeakerActor), "진수"), "ko");
        _ja = Context.ActorOf(Props.Create(typeof(JaSpeakerActor), "ハナコ"), "ja");
        base.PreStart();
    }
}
