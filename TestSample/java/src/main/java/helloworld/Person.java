package helloworld;

public abstract class Person {
    protected final String name;
    protected final String language;

    protected Person(String name, String language) {
        this.name = name;
        this.language = language;
    }

    public abstract String speak();

    public String hello() {
        return String.format("[%s] %s: %s", language, name, speak());
    }
}
