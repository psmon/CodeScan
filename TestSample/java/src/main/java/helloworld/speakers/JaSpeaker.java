package helloworld.speakers;

import helloworld.Person;

public final class JaSpeaker extends Person {
    public JaSpeaker(String name) {
        super(name, "ja");
    }

    @Override
    public String speak() {
        return "こんにちは、世界！";
    }
}
