import { Person } from "../Person.js";

export class JaSpeaker extends Person {
    constructor(name) {
        super(name, "ja");
    }

    speak() {
        return "こんにちは、世界！";
    }
}
