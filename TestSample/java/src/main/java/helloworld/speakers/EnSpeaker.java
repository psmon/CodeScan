package helloworld.speakers;

import helloworld.Person;

public final class EnSpeaker extends Person {
    public EnSpeaker(String name) {
        super(name, "en");
    }

    @Override
    public String speak() {
        return "Hello, World!";
    }
}
