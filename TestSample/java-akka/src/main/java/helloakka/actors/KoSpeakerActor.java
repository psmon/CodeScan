package helloakka.actors;

import akka.actor.Props;

public final class KoSpeakerActor extends PersonActor {
    public KoSpeakerActor(String name) {
        super(name, "ko");
    }

    public static Props props(String name) {
        return Props.create(KoSpeakerActor.class, name);
    }

    @Override
    protected String greeting() {
        return "안녕, 세상!";
    }
}
