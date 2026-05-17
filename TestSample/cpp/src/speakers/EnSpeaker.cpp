#include "EnSpeaker.h"

namespace helloworld::speakers {

EnSpeaker::EnSpeaker(std::string name)
    : Person(std::move(name), "en") {}

std::string EnSpeaker::speak() const {
    return "Hello, World!";
}

}
