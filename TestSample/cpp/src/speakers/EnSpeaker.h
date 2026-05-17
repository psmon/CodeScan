#pragma once

#include "../Person.h"

namespace helloworld::speakers {

class EnSpeaker : public Person {
public:
    explicit EnSpeaker(std::string name);
    std::string speak() const override;
};

}
