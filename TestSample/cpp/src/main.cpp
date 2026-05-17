#include <iostream>
#include <memory>

#include "World.h"
#include "speakers/EnSpeaker.h"
#include "speakers/KoSpeaker.h"
#include "speakers/JaSpeaker.h"

int main() {
    helloworld::World world;
    world.add(std::make_unique<helloworld::speakers::EnSpeaker>("Alice"));
    world.add(std::make_unique<helloworld::speakers::KoSpeaker>("진수"));
    world.add(std::make_unique<helloworld::speakers::JaSpeaker>("ハナコ"));

    for (const auto& line : world.helloAll()) {
        std::cout << line << std::endl;
    }
    return 0;
}
