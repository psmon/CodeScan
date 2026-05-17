package helloworld;

import helloworld.speakers.EnSpeaker;
import helloworld.speakers.KoSpeaker;
import helloworld.speakers.JaSpeaker;

public final class Main {
    public static void main(String[] args) {
        World world = new World();
        world.add(new EnSpeaker("Alice"));
        world.add(new KoSpeaker("진수"));
        world.add(new JaSpeaker("ハナコ"));

        for (String line : world.helloAll()) {
            System.out.println(line);
        }
    }
}
