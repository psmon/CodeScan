#pragma once

#include "../Person.h"

namespace helloworld::speakers {

class KoSpeaker : public Person {
public:
    explicit KoSpeaker(std::string name);
    std::string speak() const override;
};

}
