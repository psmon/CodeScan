from src.world import World
from src.speakers.en_speaker import EnSpeaker
from src.speakers.ko_speaker import KoSpeaker
from src.speakers.ja_speaker import JaSpeaker


def main() -> None:
    world = World()
    world.add(EnSpeaker("Alice"))
    world.add(KoSpeaker("진수"))
    world.add(JaSpeaker("ハナコ"))

    for line in world.hello_all():
        print(line)


if __name__ == "__main__":
    main()
