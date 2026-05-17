mod person;
mod world;
mod speakers;

use world::World;
use speakers::en_speaker::EnSpeaker;
use speakers::ko_speaker::KoSpeaker;
use speakers::ja_speaker::JaSpeaker;

fn main() {
    let mut w = World::new();
    w.add(Box::new(EnSpeaker::new("Alice")));
    w.add(Box::new(KoSpeaker::new("진수")));
    w.add(Box::new(JaSpeaker::new("ハナコ")));

    for line in w.hello_all() {
        println!("{}", line);
    }
}
