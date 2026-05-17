export class Person {
    constructor(name, language) {
        if (new.target === Person) {
            throw new Error("Person is abstract");
        }
        this.name = name;
        this.language = language;
    }

    speak() {
        throw new Error("speak() must be overridden");
    }

    hello() {
        return `[${this.language}] ${this.name}: ${this.speak()}`;
    }
}
