#pragma once

#include "../Person.h"

namespace helloworld::speakers {

class JaSpeaker : public Person {
public:
    explicit JaSpeaker(std::string name);
    std::string speak() const override;
};

}
