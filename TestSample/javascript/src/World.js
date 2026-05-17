export class World {
    #people = [];

    add(person) {
        this.#people.push(person);
    }

    helloAll() {
        return this.#people.map((p) => p.hello());
    }
}
