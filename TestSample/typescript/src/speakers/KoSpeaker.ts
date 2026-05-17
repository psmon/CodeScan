import { Person } from "../Person.js";

export class KoSpeaker extends Person {
    constructor(name: string) {
        super(name, "ko");
    }

    speak(): string {
        return "안녕, 세상!";
    }
}
