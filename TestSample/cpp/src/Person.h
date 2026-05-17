#pragma once

#include <string>

namespace helloworld {

class Person {
public:
    Person(std::string name, std::string language);
    virtual ~Person() = default;

    virtual std::string speak() const = 0;
    std::string hello() const;

    const std::string& name() const { return name_; }
    const std::string& language() const { return language_; }

protected:
    std::string name_;
    std::string language_;
};

}
