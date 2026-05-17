package helloakka.actors;

import akka.actor.AbstractActor;
import akka.actor.ActorRef;
import akka.actor.Props;
import helloakka.messages.HelloAll;
import helloakka.messages.HelloResponse;
import helloakka.messages.SayHello;

/**
 * 부모 액터. preStart()에서 En/Ko/Ja Speaker를 자식으로 생성하고
 * HelloAll 메시지를 받으면 모든 자식에게 SayHello를 발송한다.
 */
public final class WorldActor extends AbstractActor {
    private ActorRef en;
    private ActorRef ko;
    private ActorRef ja;

    public static Props props() {
        return Props.create(WorldActor.class);
    }

    @Override
    public void preStart() {
        // typed Props.create + name → 자식 액터 경로 /user/world/en
        en = getContext().actorOf(EnSpeakerActor.props("Alice"), "en");
        ko = getContext().actorOf(KoSpeakerActor.props("진수"), "ko");
        ja = getContext().actorOf(JaSpeakerActor.props("ハナコ"), "ja");
    }

    @Override
    public Receive createReceive() {
        return receiveBuilder()
            .match(HelloAll.class, msg -> {
                en.tell(new SayHello(), getSelf());
                ko.tell(new SayHello(), getSelf());
                ja.tell(new SayHello(), getSelf());
            })
            .match(HelloResponse.class, msg ->
                System.out.printf("[%s] %s: %s%n", msg.language(), msg.name(), msg.greeting()))
            .build();
    }
}
