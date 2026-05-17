#include "JaSpeaker.h"

namespace helloworld::speakers {

JaSpeaker::JaSpeaker(std::string name)
    : Person(std::move(name), "ja") {}

std::string JaSpeaker::speak() const {
    return "こんにちは、世界！";
}

}
