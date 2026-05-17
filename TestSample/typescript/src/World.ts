import { Person } from "./Person.js";

export class World {
    private readonly people: Person[] = [];

    add(person: Person): void {
        this.people.push(person);
    }

    helloAll(): string[] {
        return this.people.map((p) => p.hello());
    }
}
