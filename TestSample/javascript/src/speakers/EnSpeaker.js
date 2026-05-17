import { Person } from "../Person.js";

export class EnSpeaker extends Person {
    constructor(name) {
        super(name, "en");
    }

    speak() {
        return "Hello, World!";
    }
}
