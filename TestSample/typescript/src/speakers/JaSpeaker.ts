import { Person } from "../Person.js";

export class JaSpeaker extends Person {
    constructor(name: string) {
        super(name, "ja");
    }

    speak(): string {
        return "こんにちは、世界！";
    }
}
