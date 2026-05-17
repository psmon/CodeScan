import { Person } from "../Person.js";

export class KoSpeaker extends Person {
    constructor(name) {
        super(name, "ko");
    }

    speak() {
        return "안녕, 세상!";
    }
}
