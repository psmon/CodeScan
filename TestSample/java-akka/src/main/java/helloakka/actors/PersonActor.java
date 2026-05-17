package helloakka.actors;

import akka.actor.AbstractActor;
import helloakka.messages.HelloResponse;
import helloakka.messages.SayHello;

/**
 * 모든 Speaker 액터의 추상 베이스. greeting() 만 자식이 구현하면
 * SayHello 메시지 처리/응답은 공통으로 처리된다.
 */
public abstract class PersonActor extends AbstractActor {
    protected final String name;
    protected final String languageTag;

    protected PersonActor(String name, String languageTag) {
        this.name = name;
        this.languageTag = languageTag;
    }

    protected abstract String greeting();

    @Override
    public Receive createReceive() {
        return receiveBuilder()
            .match(SayHello.class, msg ->
                getSender().tell(new HelloResponse(languageTag, name, greeting()), getSelf()))
            .build();
    }
}
