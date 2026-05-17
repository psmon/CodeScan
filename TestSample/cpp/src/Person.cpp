#include "Person.h"

namespace helloworld {

Person::Person(std::string name, std::string language)
    : name_(std::move(name)), language_(std::move(language)) {}

std::string Person::hello() const {
    return "[" + language_ + "] " + name_ + ": " + speak();
}

}
