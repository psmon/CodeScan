import { World } from "./World.js";
import { EnSpeaker } from "./speakers/EnSpeaker.js";
import { KoSpeaker } from "./speakers/KoSpeaker.js";
import { JaSpeaker } from "./speakers/JaSpeaker.js";

const world = new World();
world.add(new EnSpeaker("Alice"));
world.add(new KoSpeaker("진수"));
world.add(new JaSpeaker("ハナコ"));

for (const line of world.helloAll()) {
    console.log(line);
}
