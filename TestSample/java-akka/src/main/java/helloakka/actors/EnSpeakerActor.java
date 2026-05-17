package helloakka.actors;

import akka.actor.Props;

public final class EnSpeakerActor extends PersonActor {
    public EnSpeakerActor(String name) {
        super(name, "en");
    }

    public static Props props(String name) {
        return Props.create(EnSpeakerActor.class, name);
    }

    @Override
    protected String greeting() {
        return "Hello, World!";
    }
}
