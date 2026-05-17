using Akka.Actor;
using HelloAkka.Messages;

namespace HelloAkka.Actors;

/// <summary>
/// 모든 Speaker 액터의 추상 베이스. <see cref="Greeting"/>만 자식이 구현하면
/// 메시지 핸들링과 응답 패턴은 공통으로 처리된다.
/// </summary>
public abstract class PersonActor : ReceiveActor
{
    protected string Name { get; }
    protected string LanguageTag { get; }

    protected PersonActor(string name, string languageTag)
    {
        Name = name;
        LanguageTag = languageTag;

        Receive<SayHello>(_ =>
        {
            Sender.Tell(new HelloResponse(LanguageTag, Name, Greeting()));
        });
    }

    protected abstract string Greeting();
}
