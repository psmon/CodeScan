#include "World.h"

namespace helloworld {

void World::add(std::unique_ptr<Person> person) {
    people_.push_back(std::move(person));
}

std::vector<std::string> World::helloAll() const {
    std::vector<std::string> out;
    out.reserve(people_.size());
    for (const auto& p : people_) {
        out.push_back(p->hello());
    }
    return out;
}

}
