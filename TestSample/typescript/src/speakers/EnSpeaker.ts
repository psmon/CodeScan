import { Person } from "../Person.js";

export class EnSpeaker extends Person {
    constructor(name: string) {
        super(name, "en");
    }

    speak(): string {
        return "Hello, World!";
    }
}
