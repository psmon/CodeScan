export abstract class Person {
    readonly name: string;
    readonly language: string;

    constructor(name: string, language: string) {
        this.name = name;
        this.language = language;
    }

    abstract speak(): string;

    hello(): string {
        return `[${this.language}] ${this.name}: ${this.speak()}`;
    }
}
