package helloakka.actors;

import akka.actor.Props;

public final class JaSpeakerActor extends PersonActor {
    public JaSpeakerActor(String name) {
        super(name, "ja");
    }

    public static Props props(String name) {
        return Props.create(JaSpeakerActor.class, name);
    }

    @Override
    protected String greeting() {
        return "こんにちは、世界！";
    }
}
