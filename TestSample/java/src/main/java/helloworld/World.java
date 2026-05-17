package helloworld;

import java.util.ArrayList;
import java.util.List;

public final class World {
    private final List<Person> people = new ArrayList<>();

    public void add(Person person) {
        people.add(person);
    }

    public List<String> helloAll() {
        List<String> lines = new ArrayList<>();
        for (Person p : people) {
            lines.add(p.hello());
        }
        return lines;
    }
}
