#include "KoSpeaker.h"

namespace helloworld::speakers {

KoSpeaker::KoSpeaker(std::string name)
    : Person(std::move(name), "ko") {}

std::string KoSpeaker::speak() const {
    return "안녕, 세상!";
}

}
