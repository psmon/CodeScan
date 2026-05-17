package helloworld.speakers;

import helloworld.Person;

public final class KoSpeaker extends Person {
    public KoSpeaker(String name) {
        super(name, "ko");
    }

    @Override
    public String speak() {
        return "안녕, 세상!";
    }
}
