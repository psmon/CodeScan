#pragma once

#include <memory>
#include <vector>
#include <string>
#include "Person.h"

namespace helloworld {

class World {
public:
    void add(std::unique_ptr<Person> person);
    std::vector<std::string> helloAll() const;

private:
    std::vector<std::unique_ptr<Person>> people_;
};

}
